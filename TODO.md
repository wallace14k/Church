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
- [x] Docker Compose com Postgres 17, schema e seed aplicados na criação do volume
- [x] Seed de papéis e permissões (5 papéis, 9 permissões, 16 concessões)
- [x] Logging estruturado com Serilog + correlation ID atravessando as camadas
- [x] Dockerfile multi-stage non-root e manifests Kubernetes
- [ ] **Migrations do EF Core** — `db/*.sql` é a fonte hoje, mas não há linha do tempo
      versionada. Sem elas não há caminho de atualização de schema em produção.
- [ ] CI: build, teste, lint e SAST em pipeline
- [ ] Testes de integração com Testcontainers
- [ ] **Teste que prova o isolamento cross-tenant** com o Global Query Filter desligado —
      é o portão de saída da Onda 1 no doc 06, e continua aberto

## Onda 1 — Identidade e tenancy

- [x] Domínio: `User`, `EmailVerificationCode`, `RefreshToken`, `Tenant`, `Membership`
- [x] OTP passwordless com rate limit por e-mail contado no banco
- [x] JWT RS256 com as claims documentadas
- [x] Refresh com rotação e detecção de reuso (revoga a family inteira)
- [x] RBAC + policy-based, com `Premium.Content` fora do escopo de tenant
- [x] Global Query Filters + RLS + interceptor de contexto
- [x] Verificado ponta a ponta contra PostgreSQL real
- [ ] **Dispatcher do Outbox** — as mensagens são gravadas e ninguém as lê.
      **O código OTP não chega ao usuário.** Maior buraco funcional aberto.
- [ ] Adaptador real de e-mail (`IEmailSender`) — hoje só existe o de desenvolvimento
- [ ] Endpoint de troca de igreja exposto na API (a lógica existe no handler)
- [ ] MFA para papéis administrativos (Fase 2, ver doc 05)

## Onda 2 — Núcleo do ChMS

### Membros
- [x] Tabelas `members` e `families` com RLS
- [x] Domínio `Member` com vínculo opcional a conta de login
- [x] Busca insensível a acento e caixa, com índice de trigramas
- [x] API: listar, detalhar, cadastrar
- [ ] **Telas de membros** — lista com busca, ficha, formulário de cadastro.
      A API está pronta e testada, e nada disso aparece na interface.
- [ ] Editar e inativar membro (API + tela)
- [ ] Famílias: agrupar membros, tela de família
- [ ] Aniversariantes do mês (o índice existe, falta a tela)
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
- [x] Sistema de design Portrait com assinatura em latão
- [x] Cliente de API com renovação em voo única
- [x] Storage de token divergindo por plataforma
- [x] Fluxo de autenticação: entrar, código, início
- [x] Marca desenhada e ícone do app em todos os tamanhos
- [ ] **Navegação principal** — o app não tem como sair da tela de início
- [ ] Estados de carregamento e erro padronizados nas listagens
- [ ] `FlashList` nas listas longas (a skill de performance exige, e não há lista ainda)
- [ ] Backoffice em React DOM — ver discordância **D2** em `docs/00-premissas.md`

## Decisões pendentes — bloqueiam trabalho

> Não são tarefas de engenharia. Adiá-las gera retrabalho estrutural.

- [ ] **Aceitar `public_id` ao lado da PK numérica (D1)** — já implementado na prática;
      falta a decisão formal, porque reverter depois exige migração de dados
- [ ] **App React DOM separado para o backoffice (D2)** — define a estrutura do monorepo
- [ ] **Fornecedor de mídia** — migrar 100 TB depois custa o egress que se queria evitar
- [ ] **Provedor de e-mail transacional** — bloqueia o dispatcher do Outbox
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
| `dotnet test` | 106 testes |
| `npm run typecheck` | 4 pacotes limpos |
| `npm run test` | 81 testes |
| `expo-doctor` | 21/21 |
| Login ponta a ponta | verificado contra PostgreSQL real |
| Motor de retenção | verificado em execução |
