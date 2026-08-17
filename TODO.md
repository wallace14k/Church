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
- [ ] **CI: build, teste, lint e SAST em pipeline** — `.github/workflows/ci.yml`
      escrito, com quatro jobs: `backend` (build Release + unitários +
      Testcontainers), `frontend` (typecheck + testes), `sast` (CodeQL em
      `csharp` e `javascript-typescript`, conjunto `security-extended`) e
      `dependencias` (NuGet + npm).
      **Continua aberto porque o workflow em si nunca rodou** — só executa no
      primeiro push, e a regra deste arquivo não deixa marcar o que não foi
      executado. O que *foi* verificado localmente, comando por comando: build
      Release limpo, os 160 testes em Release, `dotnet list package
      --vulnerable` (nenhum), typecheck dos 4 pacotes, e `npm run test
      --workspaces --if-present` (o `--if-present` é necessário: o app não tem
      script de teste e derrubaria o job sem ele).
      Achado no caminho: `dotnet list package --vulnerable` **sai com código 0
      mesmo achando vulnerabilidade** — ele lista, não julga. Sem a checagem no
      texto da saída, o job passaria verde para sempre.
- [ ] **Dívida: 14 vulnerabilidades `high` no npm**, todas da cadeia de build do
      Expo (`metro`, `@expo/cli`, `xcode`, via `image-size`) — ferramenta de
      quem builda, não código que chega ao bundle. O gate do CI bloqueia em
      `critical` sobre dependências de produção e **imprime** as `high` sem
      bloquear: travar nelas deixaria o CI vermelho desde o dia 1 e treinaria a
      equipe a ignorar o job, que é pior do que não ter o job. Reavaliar quando
      o Expo publicar as correções.
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
- [x] **Famílias: agrupar membros, tela de família** — domínio `Family` (novo
      agregado; a tabela já existia com RLS, só nunca fora mapeada — antes havia
      apenas `FamilyRow`, uma projeção somente-leitura). `IFamilyRepository` +
      `FamilyRepository`; API: `GET/POST /api/v1/families`, `GET
      /api/v1/families/{id}` (detalhe com membros), `PUT
      /api/v1/members/{id}/family` (vincula/desvincula, `familyId: null`
      remove). Migration `FamilyEntityMapping` tem corpo vazio de propósito —
      `created_at`/`updated_at`/a constraint de unicidade já existiam na tabela
      física (criados por `db/002_members.sql` antes de existir timeline), então
      o `Up` gerado pela ferramenta tentava recriá-los; mesmo descompasso já
      documentado na `BaselineSchema`. Corrigido no caminho: `GetAsync`,
      `UpdateAsync` e `ChangeStatusAsync` de membro nunca preenchiam
      `familyName` na resposta — a ficha do membro já lia esse campo e sempre
      mostrava vazio; um bug real, não hipotético, só visível testando contra a
      API de verdade. Tela: lista de famílias com contagem de membros, ficha de
      família com a lista de membros, cadastro; seletor de família (pílulas,
      com opção de criar nova inline) na tela de editar membro. 5 testes novos
      de domínio (`FamilyTests`). Verificado contra PostgreSQL real: criar
      família, vincular membro, contagem atualiza, ficha de família lista o
      membro, `familyName` propaga para listagem e ficha individual,
      desvincular limpa o campo, família/membro inexistente dá 404.
- [x] **Aniversariantes do mês** — tela própria (`inicio/aniversariantes.tsx`),
      empilhada sobre o painel de início na mesma pilha ("ver todos" no card do
      painel). Ordenação corrigida no caminho: `MemberRepository.ListAsync`
      ordenava por nome mesmo com `birthdayMonth` — útil para achar alguém, inútil
      para saber quem faz aniversário primeiro. Agora ordena por dia do mês quando
      o filtro está ativo. Verificado contra PostgreSQL real com nomes escolhidos
      de propósito para divergir da ordem alfabética ("Abel", dia 30, aparece por
      último; "Zulmira", dia 28, aparece antes dele) — não é coincidência de
      alfabeto batendo com data.
- [x] **Importar lista de membros de planilha** — CSV apenas, com mapeamento de
      colunas na tela (o usuário sobe a planilha que já tem, sem precisar
      adequar cabeçalhos a um modelo). `POST /api/v1/members/import` recebe
      linhas já mapeadas — o backend não sabe nem precisa saber qual coluna
      original virou o quê. Duplicado (mesmo e-mail já cadastrado no tenant,
      contra o banco ou dentro do próprio lote) é pulado e reportado, não
      atualiza o existente — decisão explícita, para não sobrescrever cadastro
      por engano. `IMemberRepository.ListEmailsAsync` faz uma consulta para o
      lote inteiro em vez de uma por linha. Teto de 500 linhas por chamada
      (`MemberEndpoints.MaxImportRows`), espelhado no cliente antes mesmo de
      enviar. Parser de CSV escrito à mão em `@congrega/core/csv` (aspas,
      vírgula/ponto e vírgula dentro de campo, quebra de linha dentro de
      campo, BOM, CRLF) — 10 testes. Tela em três passos: selecionar arquivo
      (`expo-document-picker`, novo) → mapear colunas com sugestão automática
      por nome de cabeçalho → prévia e relatório de linhas puladas com o
      motivo. Verificado contra PostgreSQL real: lote com nome vazio,
      nascimento futuro, e-mail duplicado dentro do lote e e-mail já existente
      no tenant — cada um rejeitado com o motivo certo, o resto importado;
      lote vazio dá 400.

### Financeiro
- [x] **Tabelas de lançamentos e categorias** — `db/006_financeiro.sql`, aplicado
      pela migration `FinanceiroSchema`. O corpo gerado pela ferramenta foi
      substituído pelo DDL, mesmo motivo da `BaselineSchema`: ele criava as duas
      tabelas **sem** RLS, sem os `CHECK` e sem as FK `RESTRICT`, com aparência de
      estar completo. Verificado contra o banco real (`\d giving_entries`) e, o
      que importa mais, contra um Postgres **novo** pelos testes de
      Testcontainers, que rodam todas as migrations do zero.
- [x] **Domínio de contribuição, em centavos, com FK `RESTRICT` para membro** —
      `GivingCategory` e `GivingEntry`. A decisão que organiza o módulo: **o
      sinal do dinheiro mora na categoria, nunca no valor**. Todo lançamento
      guarda centavos positivos (`CHECK amount_cents > 0`, verificado direto no
      banco) e é o `kind` da categoria que decide se soma ou subtrai. Permitir
      valor negativo criaria duas representações de "saída" e, algum dia, as
      duas apareceriam somadas no mesmo relatório. `RESTRICT` para membro por
      ADR-015, e também para categoria — apagar "Aluguel" faria doze meses de
      aluguel deixarem de somar em qualquer lugar (recusa confirmada no banco).
      Data futura é recusada: em livro-caixa é erro de digitação de ano, e sem a
      barreira o lançamento sai do fechamento sem ninguém notar. 17 testes novos.
- [x] **API de lançamento e listagem** — `GET/POST /api/v1/giving/categories`,
      `PUT /categories/{id}`, `GET/POST /entries`, `DELETE /entries/{id}`,
      `GET /closing`. Categoria repetida vira 409 pela constraint
      `uq_giving_categories_tenant_nome` (funcional, sobre `lower(name)`), não
      por `if (!existe)` — verificado: "dizimo" colide com "Dizimo". A tradução
      de `unique_violation` para `UniqueConstraintViolationException` acontece na
      Infrastructure; sem isso o projeto de API precisaria referenciar EF Core e
      Npgsql só para escrever um `catch`.
- [x] **Policy `Giving.Read` criada** — só a `Giving.Write` existia, e sem ela
      não havia como expor leitura do caixa. **A segregação de funções foi
      verificada de verdade, não presumida:** com o papel `Treasurer` removido
      da membership, o mesmo usuário lê fechamento e lançamentos (200) e recebe
      **403** ao lançar, criar categoria ou apagar — que é exatamente o que o
      seed sempre prometeu (`ChurchAdmin` só tem `giving.read`) e nunca havia
      sido exercitado. Papel restaurado ao fim.
- [x] **Telas: lançar, listar, fechar o mês** — aba Financeiro nova (barra de
      abas no celular, sidebar no web). Listagem por mês com navegação de
      período, resumo de entradas/saídas/saldo no topo e exclusão de lançamento
      digitado por engano; modal de lançamento com pílulas de categoria e forma
      de pagamento; tela de categorias (criar, desativar, reativar). Valor
      digitado passa por `parseBRL` — centavos inteiros do campo até o banco,
      nunca `float`.
- [x] **Relatório de fechamento por categoria** — agrupado **no banco**
      (`GroupBy` traduzido para SQL), não em memória: é a consulta que cresce
      todo mês. Conferido contra agregação SQL direta, valor a valor
      (`10050 + 8735 = 18785`; `18785 − 120000 = −101215`). Saldo negativo é
      tratado como informação, não erro, e aparece em vermelho.
- [x] **Vincular lançamento a membro na tela** — `SeletorDeMembro` busca por
      nome/e-mail/telefone com debounce e cancelamento da requisição anterior
      (senão a resposta de "jo" chega depois da de "joão" e substitui a lista).
      Só consulta a partir de 2 letras: sem esse piso, abrir o formulário
      dispararia uma busca de membros no caminho mais comum — o da oferta sem
      doador identificado, que é `null` por projeto, não por formulário
      incompleto. **Bug real achado ao testar:** o `POST /entries` devolvia
      `memberName: null` mesmo com membro vinculado — a listagem mostrava o
      nome, a resposta da criação não. Mesma classe do descuido de `familyName`
      já corrigido em membros; qualquer cliente que renderizasse a resposta do
      POST exibiria o lançamento como anônimo. Corrigido e verificado nos dois
      caminhos (com membro devolve o nome, sem membro devolve `null`); membro
      inexistente dá 404.
- [ ] Travar período fechado, com estorno em vez de exclusão — hoje o
      "fechamento" é relatório, não estado: nada impede editar um mês já
      prestado. É contabilidade de verdade e está na Fase 2 (doc 05)

### Calendário e células
- [x] **Eventos: tabela, domínio, API, tela** — `db/007_eventos.sql` +
      migration `EventosSchema` (DDL, não corpo gerado: RLS, o
      `CHECK (ends_at > starts_at)` e o índice da agenda são inexprimíveis no
      modelo). Domínio `CalendarEvent` — nome escolhido porque o analisador
      recusa o tipo `Event`, e com razão: o projeto já tem *eventos de domínio*,
      e as duas coisas juntas seriam ambíguas em toda leitura.
      **Sem recorrência, decidido e documentado:** RRULE + exceções +
      materialização + horário de verão não é "barato de construir", que é a
      justificativa com que o doc 05 pôs o calendário no MVP. O que a Onda 4
      precisa é de uma ocorrência concreta para ancorar o check-in, e isso a
      tabela entrega.
      **Cancelar não apaga.** O evento cancelado continua na agenda, riscado —
      apagá-lo faria quem já sabia do culto aparecer na porta da igreja
      fechada; a ausência não comunica cancelamento. Some de "próximos" e do
      filtro `includeCanceled=false`, mas nunca do histórico.
      Consulta por **sobreposição**, não contenção: pedir só o sábado 22
      devolve o retiro que começou na sexta 21 e ainda está em curso —
      verificado, e é o caso que um filtro `starts_at BETWEEN` esconderia.
      Teto de 400 dias por janela, e janela obrigatória. 14 testes novos.
- [x] **Policy `Tenant.Member`** — a agenda é informação da congregação, e o
      seed não tem `events.read` porque não há membro para quem faça sentido
      negar. A policy exige identidade verificada e vínculo ativo, sem
      permissão específica; escrever continua exigindo `events.write`
      (`ChurchAdmin` e `CellLeader` no seed). **Verificado com um usuário
      rebaixado a `Member` puro:** lê agenda e próximos (200), recebe 403 ao
      agendar e ao cancelar, 403 no caixa (não tem `giving.read`) e 200 em
      membros (tem `members.read`). Papéis restaurados ao fim.
- [ ] Pequenos grupos com hierarquia de liderança (Fase 2 no doc 05)

## Onda 3 — Monetização

- [x] Tabelas de planos, assinaturas, pagamentos, webhooks e entitlements
- [x] Máquina de estados da assinatura no domínio
- [x] **Motor de retenção** — verificado em execução: alertas D-7 enfileirados em
      e-mail e push, com deduplicação por chave
- [x] **`IPaymentGateway` — a porta** — `Application → IPaymentGateway →
      adaptador`, com o domínio sem nenhuma dependência de SDK. Inclui
      `FetchChargeAsync`, que existe para o *fetch-on-notify*. Em
      desenvolvimento há `DevelopmentPaymentGateway` (deriva o id da cobrança da
      própria chave de idempotência, então reenviar o mesmo checkout devolve a
      MESMA cobrança — um id aleatório esconderia justamente o bug que a chave
      existe para pegar). **Em produção não há adaptador registrado e a
      resolução falha no startup**, mesma postura do `IEmailSender` (premissa
      P8): subir cobrando contra um gateway falso é pior do que não subir.
      Registro em extensão própria `AddCongregaPayments(isDevelopment)`, com o
      ambiente explícito na composição.
- [ ] Adaptador **Abacate.pay** de verdade — a porta está pronta e o `else` do
      `AddCongregaPayments` é o lugar dele; falta credencial e contrato da API
- [x] **Domínio de `Payment` e `Entitlement`** — transição de mão única
      (confirmado não volta a pendente; estornado não volta a pago) e
      **idempotência em todas as operações**: `Confirm` repetido não emite um
      segundo `PaymentConfirmed`, que viraria uma segunda concessão de acesso.
      É literalmente o caso da skill de segurança — "Webhook A, A duplicado, A
      duplicado de novo" resultando em 1 evento e 0 acessos duplicados.
      `Entitlement.IsActiveOn` checa revogação **e** validade: olhar só o prazo
      é o erro que deixa um estornado assistindo até a data original.
      `ExtendTo` nunca encurta — webhook de renovação fora de ordem não tira
      dias já pagos. 23 testes novos.
- [x] **Webhook com HMAC, proteção de replay e `fetch-on-notify`** — pipeline na
      ordem da skill: assinatura → replay → schema → idempotência → persiste cru
      → processa. Três controles no HMAC, e os três testados: assinatura confere
      com o segredo, timestamp dentro da janela de 5 min, e comparação em
      **tempo constante** (`CryptographicOperations.FixedTimeEquals`) — comparar
      com `==` vaza pelo tempo de resposta quantos bytes iniciais estavam
      certos, e permite forjar a assinatura byte a byte sem conhecer o segredo.
      O timestamp entra **dentro** do que é assinado (`{t}.{payload}`); há teste
      que monta o ataque de trocar o `t` de um evento capturado e prova que
      falha. Evento com assinatura inválida **é gravado** (`signature_valid =
      false`) e não processado: descartar na porta apagaria a evidência de uma
      tentativa de forjar pagamento. Deduplicação por
      `uq_webhook_event (provider, provider_event_id)` com `ON CONFLICT DO
      NOTHING` — constraint, não consulta prévia, que tem janela sob
      concorrência. 15 testes novos.
- [x] **Concessão e revogação de entitlements a partir do pagamento** —
      `GrantEntitlementHandler`, passo **separado** da confirmação de propósito:
      juntar os dois acabaria com alguém checando `payment.Status == Paid` para
      decidir acesso, e aí um estorno deixaria de cortá-lo. Renovação estende o
      direito existente em vez de criar segunda linha; estorno revoga sem
      apagar (ADR-015). Pagamento de igreja (B2B) não concede entitlement de
      conteúdo — o que a igreja compra é o ChMS, cujo acesso vem da membership.
- [ ] Checkout com `Idempotency-Key` — endpoint da API ainda não exposto; o
      domínio, a constraint `uq_pay_idempotency_key` e a porta já estão prontos
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
| `dotnet test` | 201 testes (133 domínio + 50 aplicação + 18 integração, dos quais 3 com Testcontainers) |
| `npm run typecheck` | 4 pacotes limpos |
| `npm run test` | 108 testes |
| `expo-doctor` | 21/21 |
| Login ponta a ponta | verificado contra PostgreSQL real, com `congrega_app` |
| Isolamento cross-tenant | **verificado com RLS real** — GQF desligado não vaza (Testcontainers) |
| Motor de retenção | verificado em execução |
| Dispatcher do Outbox | verificado em execução — fila drenada, com `congrega_worker` |
| Membros | listar, buscar, detalhar, cadastrar, **editar e inativar** verificados |
| Famílias | criar, listar, detalhar, vincular/desvincular membro verificados contra PostgreSQL real |
| Importar planilha | CSV, mapeamento de colunas, duplicado/erro por linha verificados contra PostgreSQL real |
| Financeiro | categorias, lançamentos, exclusão e fechamento verificados; soma conferida contra SQL direto |
| Segregação de funções | **verificada**: `ChurchAdmin` lê o caixa (200) e recebe 403 ao lançar; `Member` lê a agenda e recebe 403 ao agendar |
| Agenda | criar, listar por mês, cancelar/reativar, apagar verificados; fuso e sobreposição conferidos no banco |
| Aniversariantes do mês | tela própria verificada; ordenação por dia confirmada contra PostgreSQL real |
| `/auth/tenants` | verificado contra PostgreSQL real |
| Bundle web (Metro) | recompila limpo a cada troca de design; sidebar e telas confirmadas no bundle |
| Assinatura de webhook | HMAC, replay e tempo constante verificados; ataque de trocar o timestamp falha |
| Idempotência de pagamento | verificada no domínio: `Confirm` repetido emite 1 evento, não 2 |
| CI (`.github/workflows/ci.yml`) | **escrito, nunca executado** — comandos validados um a um localmente |
