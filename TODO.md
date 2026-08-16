# Congrega — o que falta

> Estado real, verificado contra o banco, a API e o app em execução.
> Sequenciamento e justificativa das ondas em [`docs/06-riscos-e-ondas.md`](docs/06-riscos-e-ondas.md).
>
> **Regra deste arquivo:** só marque concluído o que foi *executado*, não o que foi escrito.
> Código que compila mas nunca rodou continua aberto.

---

## Onda 0 — Fundação

- [x] Monorepo, solution .NET, workspaces do frontend
- [x] DDL PostgreSQL completo (26 tabelas)
- [x] Docker Compose com Postgres 17; schema vem das migrations do EF Core
- [x] Seed de papéis e permissões (5 papéis, 9 permissões, 16 concessões)
- [x] Logging estruturado com Serilog + correlation ID atravessando as camadas
- [x] Dockerfile multi-stage non-root e manifests Kubernetes
- [x] **Migrations do EF Core** — baseline executa `db/*.sql` em ordem (RLS, índices
      parciais, funções e checks entram como `Sql()`, já que o modelo mapeia 11 das
      26 tabelas); seed de roles/permissions em migration própria. Verificado: banco
      criado do zero tem a mesma contagem de tabelas, policies, índices, funções e
      checks que o banco de desenvolvimento. `db/005_baseline_aplicado.sql` adota
      bancos existentes sem reexecutar o DDL.
- [ ] CI: build, teste, lint e SAST em pipeline
- [x] **Testes de integração com Testcontainers** — `Congrega.Infrastructure.IntegrationTests`,
      Postgres 17 real em container, migrado pelo mesmo `Database.MigrateAsync()` da produção
- [x] **Teste que prova o isolamento cross-tenant** com o Global Query Filter desligado —
      portão de saída da Onda 1, fechado. Achado real ao escrevê-lo: `congrega_app` e
      `congrega_worker` (ADR-006) nunca tinham sido criadas — a API rodava com a
      credencial dona das tabelas, que o Postgres deixa atravessar RLS por padrão. O
      RLS inteiro era decorativo; só o Global Query Filter isolava de verdade. Corrigido
      pelas migrations `AppRoles` e `MembershipsSelfServiceRls` (esta última fecha um
      segundo bug: a policy de `memberships` só liberava por `tenant_id`, e o login
      resolve o tenant *antes* de selecioná-lo — precisa também de `OR user_id`, como
      `subscriptions`/`payments`/`notification_queue` já tinham). Também exigiu
      `IAuthenticationContextWriter`: `VerifyOtpHandler`/`RefreshSessionHandler` rodam em
      endpoints anônimos e não tinham como informar ao interceptor de conexão qual
      usuário acabou de autenticar — sem isso, `app.user_id` chegava vazio ao Postgres e
      a resolução de tenant no login voltava silenciosamente vazia sob RLS real.
      Verificado com API e Workers reconectados às novas roles: login, `/auth/tenants`
      e Outbox continuam funcionando; 118 testes passam (65 domínio + 50 aplicação + 3
      integração).
- [x] **Segundo bug de RLS, achado ao testar `/api/v1/members` de verdade** —
      `TenantContextMiddleware` consultava `memberships.FindActiveAsync` para
      validar a claim de tenant *antes* de atribuir `UserId` ao contexto da
      requisição. Com RLS real, essa consulta específica ia com `app.user_id`
      vazio, a policy `tenant_id = ... OR user_id = ...` nunca casava, e **todo
      usuário autenticado passava a ser tratado como sem vínculo em toda
      requisição** — um 403 em qualquer endpoint com `TenantScopedRequirement`,
      para qualquer um. Não apareceu nos testes anteriores porque
      `/auth/tenants` não exige tenant e o login usa outro caminho, já corrigido
      com `IAuthenticationContextWriter`. Corrigido chamando
      `tenantContext.Assign(userId, tenantId: null)` antes da consulta de
      membership, não só depois. Verificado: `GET`/`POST`/`PUT /members` voltam
      a responder 200/201 com o mesmo usuário que antes dava 403.

## Onda 1 — Identidade e tenancy

- [x] Domínio: `User`, `EmailVerificationCode`, `RefreshToken`, `Tenant`, `Membership`
- [x] OTP passwordless com rate limit por e-mail contado no banco
- [x] JWT RS256 com as claims documentadas
- [x] Refresh com rotação e detecção de reuso (revoga a family inteira)
- [x] RBAC + policy-based, com `Premium.Content` fora do escopo de tenant
- [x] Global Query Filters + RLS + interceptor de contexto
- [x] Verificado ponta a ponta contra PostgreSQL real
- [x] **Dispatcher do Outbox** — verificado em execução: 47 mensagens represadas
      drenadas em um ciclo, e um OTP novo entregue em ~4,4 s
- [ ] Adaptador real de e-mail (`IEmailSender`) — o de desenvolvimento existe e
      escreve no log; produção falha no startup sem um real, de propósito
- [x] **Endpoint de troca de igreja exposto na API** — `GET /api/v1/auth/tenants`
      lista as igrejas com vínculo ativo (alimenta a tela de seleção); a troca em si
      já existia em `POST /api/v1/auth/refresh` com `SwitchToTenantId`. Verificado
      contra PostgreSQL real: usuário com duas igrejas recebe a lista correta,
      usuário sem vínculo recebe `[]`, requisição sem token recebe `401`
- [ ] MFA para papéis administrativos (Fase 2, ver doc 05)

## Onda 2 — Núcleo do ChMS

### Membros
- [x] Tabelas `members` e `families` com RLS
- [x] Domínio `Member` com vínculo opcional a conta de login
- [x] Busca insensível a acento e caixa, com índice de trigramas
- [x] API: listar, detalhar, cadastrar
- [x] **Telas de membros** — lista com busca e paginação infinita, ficha e
      formulário de cadastro, verificados contra a API real
- [x] **Editar e inativar membro (API + tela)** — `PUT /api/v1/members/{id}` edita
      nome/e-mail/telefone/nascimento/endereço via `Member.UpdateProfile` (domínio
      já tinha o método, nunca chamado); `PUT /api/v1/members/{id}/status` ativa,
      inativa, marca transferido ou falecido via `Member.ChangeStatus`. Tela modal
      de edição reaproveita o formulário de cadastro, mais um botão de
      inativar/reativar na base. Verificado contra PostgreSQL real: editar altera
      os campos e some da listagem padrão só depois de inativar, `status=Todos`
      continua mostrando; status inválido dá 400, membro inexistente dá 404.
      8 testes novos de domínio (`MemberTests`).
- [ ] Famílias: agrupar membros, tela de família
- [x] **Aniversariantes do mês** — tela própria (`inicio/aniversariantes.tsx`),
      empilhada sobre o painel de início na mesma pilha ("ver todos" no card do
      painel). Ordenação corrigida no caminho: `MemberRepository.ListAsync`
      ordenava por nome mesmo com `birthdayMonth` — útil para achar alguém, inútil
      para saber quem faz aniversário primeiro. Agora ordena por dia do mês quando
      o filtro está ativo. Verificado contra PostgreSQL real com nomes escolhidos
      de propósito para divergir da ordem alfabética ("Abel", dia 30, aparece por
      último; "Zulmira", dia 28, aparece antes dele) — não é coincidência de
      alfabeto batendo com data.
- [ ] Importar lista de membros de planilha — é o primeiro dia de uso de toda igreja

### Financeiro
- [ ] Tabelas de lançamentos e categorias
- [ ] Domínio de contribuição, em centavos, com FK `RESTRICT` para membro
- [ ] API de lançamento e listagem
- [ ] Telas: lançar, listar, fechar o mês
- [ ] Relatório de fechamento por categoria

### Calendário e células
- [ ] Eventos: tabela, domínio, API, tela
- [ ] Pequenos grupos com hierarquia de liderança (Fase 2 no doc 05)

## Onda 3 — Monetização

- [x] Tabelas de planos, assinaturas, pagamentos, webhooks e entitlements
- [x] Máquina de estados da assinatura no domínio
- [x] **Motor de retenção** — verificado em execução: alertas D-7 enfileirados em
      e-mail e push, com deduplicação por chave
- [ ] `IPaymentGateway` e adaptador Abacate.pay
- [ ] Checkout com `Idempotency-Key`
- [ ] Webhook com HMAC, proteção de replay e `fetch-on-notify`
- [ ] Concessão e revogação de entitlements a partir do pagamento
- [ ] Telas de assinatura e cobrança

## Onda 4 — Check-in infantil

> Entra por último dentro do MVP, e em piloto fechado. Ver os portões
> obrigatórios em [`docs/05-escopo.md`](docs/05-escopo.md).

- [ ] Tabelas de crianças, responsáveis e check-in
- [ ] Fila offline em SQLite com idempotency key estável
- [ ] Etiqueta com `public_id` opaco — nunca ID sequencial impresso
- [ ] Código de retirada hasheado, de uso único e com TTL
- [ ] Criptografia em coluna para alergia, foto e observações
- [ ] Log de auditoria em toda leitura de ficha
- [ ] Fluxo de consentimento parental com registro de prova
- [ ] Alerta em tempo real de retirada com código inválido
- [ ] Parecer jurídico sobre o Art. 14 da LGPD

## Onda 5 — Congrega+ (Fase 2)

- [ ] Catálogo: trilhas, aulas, eBooks, packs
- [ ] Cloudflare R2 e URLs assinadas com TTL curto
- [ ] Watermark por sessão
- [ ] Paywall divergindo por plataforma
- [ ] IAP no iOS e Play Billing no Android, com validação de recibo server-side
- [ ] Leitor de eBook e player de vídeo

---

## Frontend — transversal

- [x] Monorepo com `@congrega/core`, `@congrega/ui`, `@congrega/api-client`
- [x] Sistema de design — trocado duas vezes nesta sessão: Portrait (marinho +
      latão) → Steep (serifada + pêssego, `DESIGN_new.md`) → o sistema atual,
      inspirado no padrão de dashboard SaaS que o cliente pediu para seguir
      (sans-serif único, acento índigo, cartão branco com borda fina). Tokens de
      contraste WCAG AA recalculados e testados a cada troca.
- [x] **Sidebar de verdade no web** (`(tabs)/_layout.web.tsx`, resolvido por
      extensão de plataforma do Metro) — recolhe para só ícones com dica
      flutuante no hover, estado lembrado via `localStorage`; celular continua
      com barra de abas nativa (`(tabs)/_layout.tsx`, sem `.web`). Seletor de
      igreja no topo usa o `/auth/tenants` real.
- [x] Cliente de API com renovação em voo única
- [x] Storage de token divergindo por plataforma
- [x] Fluxo de autenticação: entrar, código, início
- [x] Marca desenhada e ícone do app em todos os tamanhos
- [x] **Painel de início com dados reais** — contagem de membros e
      aniversariantes do mês (`useDashboard`), não mais um aviso de "em
      construção"
- [x] Estados de carregamento, erro e vazio na lista de membros
- [ ] Padronizar esses estados nas demais listagens quando existirem
- [x] `FlashList` na lista de membros
- [ ] Backoffice em React DOM — ver discordância **D2** em `docs/00-premissas.md`

## Decisões pendentes — bloqueiam trabalho

> Não são tarefas de engenharia. Adiá-las gera retrabalho estrutural.

- [ ] **Aceitar `public_id` ao lado da PK numérica (D1)** — já implementado na prática;
      falta a decisão formal, porque reverter depois exige migração de dados
- [ ] **App React DOM separado para o backoffice (D2)** — define a estrutura do monorepo
- [ ] **Fornecedor de mídia** — migrar 100 TB depois custa o egress que se queria evitar
- [ ] **Provedor de e-mail transacional** — o dispatcher já funciona; falta o
      adaptador real para o e-mail sair de verdade em produção
- [ ] **Parecer jurídico** sobre Arts. 11 e 14 da LGPD

## Imagens

- [x] Colagem do hero (5 cenas)
- [x] Vazio de contribuições
- [x] Ícone do app
- [ ] **Vazio de membros** — livro de registro manuscrito. Prompt já enviado.

---

## Verificação — o que roda hoje

| Item | Estado |
|---|---|
| `dotnet build` | 0 avisos, 0 erros |
| `dotnet test` | 126 testes (73 domínio + 50 aplicação + 3 integração/Testcontainers) |
| `npm run typecheck` | 4 pacotes limpos |
| `npm run test` | 84 testes |
| `expo-doctor` | 21/21 |
| Login ponta a ponta | verificado contra PostgreSQL real, com `congrega_app` |
| Isolamento cross-tenant | **verificado com RLS real** — GQF desligado não vaza (Testcontainers) |
| Motor de retenção | verificado em execução |
| Dispatcher do Outbox | verificado em execução — fila drenada, com `congrega_worker` |
| Membros | listar, buscar, detalhar, cadastrar, **editar e inativar** verificados |
| Aniversariantes do mês | tela própria verificada; ordenação por dia confirmada contra PostgreSQL real |
| `/auth/tenants` | verificado contra PostgreSQL real |
| Bundle web (Metro) | recompila limpo a cada troca de design; sidebar e telas confirmadas no bundle |
