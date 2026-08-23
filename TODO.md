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
- [x] **Checkout com `Idempotency-Key` exposto** — `POST /api/v1/billing/checkout`,
      policy `Billing.Checkout` (identidade verificada, **sem escopo de tenant**: o
      Congrega+ é da pessoa, e exigir vínculo com igreja impediria de assinar
      justamente quem o produto B2C existe para atender). Três decisões que o
      caminho feliz não exercita, cada uma com teste que falha se for removida:
      **(1) o preço vem do banco** — o cliente manda só `planCode`; aceitar
      `amountCents` do corpo é a adulteração de preço que nenhum cliente honesto
      revela. **(2) a chave é prefixada pelo titular** (`u{userId}:{chave}`) —
      `uq_pay_idempotency_key` é UNIQUE sobre a tabela inteira, e sem o prefixo
      duas pessoas escolhendo `"1"` colidiriam: a segunda receberia de volta a
      cobrança da primeira, com o `public_id` dela. **(3) audiência do plano é
      controle, não rótulo de catálogo** — sem conferir `plans.audience`, bastava
      o código para uma pessoa física abrir cobrança do plano B2B da igreja;
      plano inexistente e audiência errada respondem **idêntico**, senão a
      resposta vira oráculo de quais códigos de plano existem.
      Verificado contra a API real: 201 na criação, **200 com o MESMO
      `paymentId` na repetição da chave** e uma linha só em `payments`, 404 para
      plano B2B, 404 idêntico para inexistente, 400 sem `Idempotency-Key`, 401
      sem token. 9 testes novos.
- [x] **Bug achado ao ligar o checkout: `Subscription` nunca teve mapeamento** —
      a entidade não tinha `IEntityTypeConfiguration`, então o EF caía na
      convenção padrão e emitia `SELECT ... FROM "Subscriptions"`, tabela que não
      existe num schema inteiramente snake_case. O `ISubscriptionStore` compilava
      e tinha teste verde com dublê **desde a Onda 3**, sem nunca ter executado
      uma consulta real; apareceu como `42P01` no primeiro checkout de verdade.
      Corrigido com `SubscriptionConfiguration` + migration
      `SubscriptionEntityMapping` de corpo vazio — o scaffold leu a diferença
      como renomeação e gerou `RenameTable` + treze `RenameColumn` sobre uma
      tabela que nunca existiu. Terceira ocorrência do mesmo descompasso
      (`FamilyEntityMapping`, `BillingEntityMapping`).
- [x] **Seed de planos** (`db/008_planos.sql` + migration `SeedPlanos`) — a tabela
      `plans` estava **vazia**, então o checkout responderia "plano indisponível"
      para qualquer código: sobe, autentica, valida e nunca cobra. Mesma classe
      de falha silenciosa do seed de papéis, e por isso também é migration.
- [x] **Webhook exposto em HTTP** — `POST /api/v1/billing/webhook`, anônimo (o
      gateway não tem JWT nosso; a autenticação dele é o HMAC). Lê o **corpo
      cru** com teto de 64 KB — deixar o binder desserializar e reserializar
      mudaria os bytes e o HMAC deixaria de conferir para todo evento legítimo.
      **A borda registra e devolve; quem processa é o worker.** Não é preferência
      de arquitetura, é restrição de RLS: a requisição é anônima, logo sem
      `app.user_id`, e a policy de `payments` filtra por titular — tocar em
      `payments` daqui não daria erro barulhento, daria **zero linhas**, e todo
      webhook legítimo concluiria "pagamento local não encontrado".
      `payment_webhooks` não tem RLS, e é por isso que registrar funciona e
      processar não. Handler próprio (`ReceivePaymentWebhookHandler`).
      Verificado contra a API real: assinatura válida **202**, reentrega **200**
      sem segunda linha, corpo adulterado **400**, sem assinatura **400**, replay
      de ontem **400** — e os quatro inválidos **gravados** com
      `signature_valid = false`, que é a evidência da tentativa de forjar
      pagamento. O vão na sequência de `id` prova que o `ON CONFLICT` da
      deduplicação disparou no banco, não em código.
- [x] **Processador de webhook no worker** — `WebhookDispatcherService`
      (`Congrega.Workers`) drena `payment_webhooks` com o mesmo padrão de
      reivindicação atômica do Outbox (`UPDATE ... FROM (SELECT ... FOR UPDATE
      SKIP LOCKED) ... RETURNING`, incrementando `process_attempts` na própria
      reivindicação), filtrando `signature_valid = true AND process_attempts <
      maxAttempts`. Rodando com `congrega_worker`.
      **Três achados no caminho, nenhum deles hipotético:**
      (1) `ProcessPaymentWebhookHandler` tinha **zero testes** apesar deste
      arquivo dizer o contrário, e do jeito que estava escrito **nunca
      processaria nada** se ligado a um dispatcher — repetia a verificação de
      assinatura e o `TryRecordAsync` que a borda (`ReceivePaymentWebhookHandler`)
      já tinha feito; chamado sobre uma linha já persistida, o `ON CONFLICT DO
      NOTHING` devolveria sempre "duplicado", sem nunca chegar ao
      fetch-on-notify. Reescrito para operar sobre `PendingPaymentWebhook` (o
      que já foi registrado e validado), não sobre a requisição crua.
      (2) Mesmo com o worker rodando, pagamento confirmado **não virava
      acesso**: `PaymentConfirmed`/`PaymentRefunded` caíam no Outbox sem
      handler registrado — `GrantEntitlementHandler` existia, testado agora
      pela primeira vez (9 testes novos, `GrantEntitlementHandlerTests`), mas
      nunca tinha sido ligado como `IOutboxMessageHandler`. Dois adaptadores
      finos (`PaymentConfirmedOutboxHandler`/`PaymentRefundedOutboxHandler`)
      fecham essa lacuna.
      (3) `Congrega.Workers` não tinha EF Core — `IPaymentRepository`,
      `IEntitlementRepository` e `ISubscriptionStore` são baseados em
      `CongregaDbContext`, que só a API registrava. Trazer
      `AddCongregaInfrastructure` inteiro acoplaria o Workers a
      `AuthenticationOptions` (chave privada do JWT) só para abrir uma conexão
      de banco. Separado em `AddCongregaPersistence` (os dois hosts) +
      `AddCongregaInfrastructure` (só a API, por cima). `WorkerTenantContext`
      novo — cross-tenant fixo, o mesmo padrão que os testes de integração já
      usavam para simular isso, agora como implementação real.
      10 testes novos de `ProcessPaymentWebhookHandler` (fakes) + 4 de
      integração com Testcontainers provando a reivindicação contra Postgres
      real, inclusive que uma linha travada por outra transação **não** é
      reivindicada (a prova de que `SKIP LOCKED` funciona de verdade, não só
      compila).
      **Verificado contra a stack real, ao vivo**: webhook assinado com HMAC
      de desenvolvimento → `202`, linha entra em `payment_webhooks` com
      `signature_valid = true` e `processed_at` vazio → dispatcher reivindica
      no ciclo seguinte → `processed_at` preenchido, sem erro. Três eventos
      com assinatura inválida (deixados de uma verificação anterior) seguem
      intocados (`processed_at` vazio, `process_attempts = 0`), confirmando
      que o filtro nunca processa evento forjado.
      **Limitação conhecida, não desta mudança**: `DevelopmentPaymentGateway`
      guarda estado em memória por processo — API e Workers são processos
      separados, então o caminho "cobrança paga de verdade → entitlement
      concedido" não dá para provar com os dois rodando ao vivo em
      desenvolvimento (o fetch-on-notify do worker sempre acha "cobrança
      desconhecida" para uma cobrança criada pelo processo da API). Esse ramo
      está coberto pelos testes de unidade (fakes determinísticos), não pela
      execução ao vivo.
- [x] **Telas de assinatura e cobrança** — aba "Congrega+" nova (`(tabs)/assinatura/`),
      sem gate de papel (ao contrário de Financeiro): é a assinatura da pessoa,
      não da igreja, e aparece mesmo para quem não tem vínculo com nenhuma
      congregação. Dois GETs novos e finos, no mesmo padrão sem Application
      Handler de `GET /auth/tenants` — `GET /billing/subscription` (assinatura
      ativa do titular, `hasSubscription:false` em vez de 404 quando não há
      nenhuma) e `GET /billing/plans` (catálogo B2C, exigiu
      `IPlanRepository.ListActiveAsync` novo). A tela mostra o status
      (`describeRenewal`, escrita na Onda 4 anterior para exatamente este uso
      e nunca antes chamada) quando há assinatura, ou a vitrine de planos com
      checkout de verdade quando não há.
      **Escopo deliberadamente cortado**: cancelar assinatura pela tela e
      histórico de pagamentos ficaram de fora — viram bullets próprios abaixo,
      não bloqueiam esta entrega.
      **Dois bugs reais, achados só porque este foi o primeiro código de
      frontend a exercitar esses dois caminhos pelo navegador:**
      (1) **CORS nunca liberava `Idempotency-Key`** — o checkout exige esse
      cabeçalho desde a Onda 3, mas só tinha sido chamado por `curl` (sem
      preflight) ou por testes de unidade. A primeira vez que o navegador
      tentou, o preflight `OPTIONS` recusou silenciosamente e o `POST` nunca
      saiu — sem esse cabeçalho na política de CORS, nenhum checkout pelo
      Congrega+ jamais teria funcionado no app web, mesmo com o backend
      inteiro correto.
      (2) **`StartCheckoutHandler` devolvia 500 ao tentar assinar um segundo
      plano com o primeiro ainda pendente** — `uq_sub_active_user` permite só
      uma assinatura não-terminal por pessoa, mas `FindReusableForCheckoutAsync`
      filtra pelo plano pedido e não encontra a existente (de outro plano); o
      `INSERT` colidia com a constraint e a exceção subia sem tratamento.
      Corrigido capturando `UniqueConstraintViolationException` por
      `ConstraintName` — mesmo padrão já usado para a corrida da chave de
      idempotência — e devolvendo `409` com mensagem clara em vez de erro
      genérico. 1 teste novo.
      Verificado ao vivo pela UI real (Playwright): conta com assinatura
      ativa mostra plano + "Vence em N dias"; conta nova mostra a vitrine de
      3 planos; clicar em "Assinar" abre cobrança de verdade (`POST
      /checkout` real) e mostra valor + código PIX; item "Congrega+" aparece
      na sidebar com o ícone certo e navega.
- [x] **Cancelar assinatura e histórico de pagamentos na tela** —
      `POST /billing/subscription/cancel` e `GET /billing/payments`, ambos sob
      `Billing.Checkout`. A assinatura e o histórico são resolvidos **pela claim
      `sub`**, nunca por id no corpo ou na query: não existe `?userId=` para
      trocar, que é a defesa contra IDOR recomendada pela §5 da skill de
      segurança — não oferecer o parâmetro, em vez de validá-lo depois.
      Cancelar chama `Subscription.Cancel` **sem `immediate`**: a renovação para,
      `CurrentPeriodEnd` não se move e os entitlements seguem válidos. A tela diz
      isso na confirmação, porque "tem certeza?" seco faria supor perda do que já
      foi pago.
      **Bug real achado ao executar, não ao ler:** depois de cancelar, o
      `GET /subscription` seguinte devolvia `hasSubscription: false` e a tela
      voltava para a **vitrine de planos** — o paywall — para alguém que ainda
      tinha acesso pago. A causa era `FindActiveByUserAsync` filtrar
      `Active | PastDue | Grace` e omitir `Canceled`; o nome estava certo e o
      comportamento errado, porque cancelar não é o fim do acesso, é o fim da
      renovação (§6 do doc 03). Renomeado para `FindCurrentByUserAsync` — o
      rename foi o instrumento, não enfeite: `Active` no nome era o que tornava
      a omissão plausível de ler, mesma disciplina do `brand` → `surfaceAccent`.
      **Segundo achado: `Subscription` — a máquina de estados inteira — tinha
      zero testes**, apesar de marcada como concluída neste arquivo; só aparecia
      como preparo de cenário em `RetentionAlertTests`. Deixou de ser teórico ao
      expor `Cancel` por HTTP, porque é a tabela de transições que decide entre
      `200` e `409`. 15 testes novos (`SubscriptionTests`) fixam as transições
      contra o diagrama da §6 — inclusive que `Grace` **não** cancela (a
      cobrança já falhou, não há renovação a cancelar), caso alcançável pela
      tela e que sem tratamento subiria como 500. A tela também não oferece o
      botão nesse estado: porta que não abre não se mostra.
      **Índice que faltava:** `payments` tinha `ix_pay_tenant` para o titular
      pessoa jurídica e **nada** equivalente para o B2C, então o histórico seria
      Seq Scan na tabela inteira a cada abertura da aba — custo que cresce com o
      número de clientes, não com o histórico de quem olha. `ix_pay_user`
      (`db/009_*.sql` + migration `IndicePagamentosPorTitular`, corpo escrito à
      mão porque índice parcial não é exprimível no modelo). `EXPLAIN` confirma
      Index Scan **sem nó de Sort** — a ordem `created_at DESC` do índice casa
      com a da consulta.
      6 testes de integração novos (`SubscriptionStoreTests`, Testcontainers)
      cobrindo os quatro estados que devem voltar e os dois que não;
      **verificados como regressão de verdade**: reintroduzi a omissão do
      `Canceled` e só o caso `Canceled` falhou.
      Verificado ao vivo pela UI real: assinatura ativa mostra botão de cancelar
      e histórico (R$ 299,00 · Pago); confirmar cancela; **após recarregar** a
      tela mostra "Cancelada — o acesso continua até o fim do período já pago"
      com **zero** botões "Assinar" na tela; cancelar de novo devolve 409 com
      mensagem clara, não 500.
- [ ] Trocar de plano pela tela — hoje o checkout de um segundo plano com o
      primeiro em andamento responde 409 (`uq_sub_active_user`). Falta decidir o
      que acontece com o período já pago do plano anterior; sem essa decisão,
      recusar é mais honesto do que cobrar duas vezes

## Onda 4 — Check-in infantil

> Entra por último dentro do MVP, e em piloto fechado. Ver os portões
> obrigatórios em [`docs/05-escopo.md`](docs/05-escopo.md).

- [x] **Tabelas de crianças, responsáveis e check-in + domínio + criptografia** —
      `db/011_criancas.sql` + migration `CriancasSchema` (DDL à mão pelo mesmo
      motivo de `FinanceiroSchema`/`EventosSchema`; aqui o descompasso seria
      mais caro, porque tabela sem `ENABLE ROW LEVEL SECURITY` tem a mesma
      aparência de tabela protegida e a diferença é a ficha de alergia de uma
      criança visível para outra igreja). Cinco tabelas com RLS: `children`,
      `child_guardians`, `child_checkins`, `parental_consents`,
      `child_access_log`.
      **Por que a Onda 4 pôde começar:** o doc 05 diz "portões obrigatórios
      antes de qualquer **liberação**", e seis dos sete são entregáveis de
      engenharia — construí-los *é* o trabalho. Só o parecer jurídico é externo,
      e ele barra o piloto, não o código.
      **Quatro decisões assadas no schema, nenhuma acrescentável depois sem
      migrar dado:** (1) campos sensíveis são `BYTEA` cifrado **na aplicação**
      (AES-256-GCM) e não `TEXT` — não é `pgcrypto`, que receberia a chave como
      argumento de função e a deixaria no log de query, exatamente onde o
      ADR-014 diz que ela não pode estar; (2) `public_id` UUID em criança e
      check-in, porque é o que vai impresso na etiqueta; (3) código de retirada
      guardado como HMAC com pepper próprio — em texto claro, o dump do banco é
      a lista de senhas de retirada de todas as crianças da plataforma;
      (4) `idempotency_key` UNIQUE, a chave estável que a fila offline
      reapresenta.
      **Pepper separado do OTP**, de propósito: rotacionar o de autenticação
      invalidaria todos os códigos de retirada em circulação no meio de um
      culto, e quem fizesse a rotação não teria como prever isso.
      **Domínio** (`Congrega.Domain/Childcare/`): `Child` — que **nunca vê o
      texto claro** dos campos sensíveis, só `byte[]`, então não há como vazar
      alergia num `ToString()` ou serializador —, `ChildCheckIn` com o ciclo do
      código, e `ParentalConsent` com a versão do texto consentido (sem ela é
      impossível demonstrar depois *a que* a pessoa consentiu). Ser responsável
      e poder retirar são campos distintos: um acordo de guarda pode registrar o
      pai e não autorizá-lo a buscar.
      **A ordem de verificação da retirada é autorização → validade → código**,
      não o contrário: conferir o código primeiro faria a resposta virar oráculo
      — quem tem o código certo mas não a autorização receberia erro diferente
      de quem errou o código, e a diferença ensina.
      16 testes de domínio + 9 de integração. Verificado contra Postgres real,
      incluindo o **critério de aceitação escrito no próprio ADR-014**: um
      `SELECT` cru na coluna de alergia devolve bytes sem nenhum pedaço
      reconhecível do texto; o mesmo texto cifrado duas vezes produz bytes
      diferentes (prova o nonce aleatório); adulterar um byte faz a decifragem
      lançar `AuthenticationTagMismatchException` em vez de devolver lixo; RLS
      impede uma igreja de ver criança de outra com `congrega_app`.
      **O portão de configuração foi verificado falhando:** subi a API sem os
      segredos e ela **recusou iniciar** com `OptionsValidationException`, em
      vez de gravar alergia em texto claro sem nada acusar. Segredos de
      desenvolvimento provisionados depois, via `user-secrets`.
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
- [x] Sistema de design — trocado **três** vezes: Portrait (marinho + latão) →
      Steep (serifada + pêssego) → Mercury (sans único, índigo, cartão branco de
      12px com sombra) → **Perk**, o atual: lima elétrico sobre neutros quentes,
      cartão pergaminho de 28px sobre canvas branco, Inter em dois pesos, zero
      sombra. Documento em `docs/07-design-system.md`, com os desvios D1–D7
      registrados; a skill de refatoração de UI ficou em
      `.agents/skills/ui-redesign/`.
      **A troca não foi só de superfície desta vez.** O token `brand` servia como
      preenchimento *e* como cor de texto de link. O lima mede **1,19:1** sobre
      branco: trocar só o valor mantendo o nome deixaria cada
      `color: colors.brand` invisível — 11 usos, nenhum erro de compilação. O
      token foi **renomeado** para `surfaceAccent` justamente para quebrar o
      build em cada um e forçar uma decisão. Consequências: link vira tinta com
      sublinhado (`TextLink` novo), ícone de aba ativa vira tinta, e estado de
      seleção vira **preenchimento** e não borda colorida (`Chip` novo, que
      absorveu quatro cópias quase idênticas espalhadas pelas telas) — borda
      lima reprovaria os 3:1 da WCAG 1.4.11 para componente não textual.
      27 testes de token, incluindo um que garante que **nenhum token de texto
      recebe o lima** e outro que veta peso 600.
      **Verificado visualmente nesta sessão** — stack completa no ar (Postgres,
      API, Workers, Expo web), login real pela UI (e-mail → código → sessão) e
      navegação por `entrar`, `código`, `início`, `membros`, `financeiro`,
      `agenda`, `financeiro/lançar`, `membros/novo`, `membros/famílias`
      capturadas com Chromium via Playwright. Pergaminho sobre canvas branco,
      lima só como preenchimento (botão primário, item ativo da sidebar, chip
      selecionado), link sublinhado, cartão de 28px sem sombra — tudo conforme
      o documento.
- [x] **Sidebar de verdade no web** (`(tabs)/_layout.web.tsx`, resolvido por
      extensão de plataforma do Metro) — recolhe para só ícones com dica
      flutuante no hover, estado lembrado via `localStorage`; celular continua
      com barra de abas nativa (`(tabs)/_layout.tsx`, sem `.web`). Seletor de
      igreja no topo usa o `/auth/tenants` real.
- [x] Cliente de API com renovação em voo única
- [x] **Dois bugs reais de sessão, achados ao logar pela UI de verdade pela
      primeira vez** (todo teste anterior era curl direto na API, nunca através
      do navegador):
      **(1) cookie `Secure` sempre `true`** — em desenvolvimento o app web fala
      com a API por `http://localhost`, e o navegador descarta em silêncio um
      cookie `Secure` recebido por HTTP. O login parecia funcionar (a sessão
      ficava em memória), e caía no primeiro reload: a hidratação via
      `/auth/refresh` não achava cookie nenhum para reapresentar. `Secure`
      agora cede só em `IsDevelopment()` (`AuthEndpoints.BuildSessionResult`),
      no mesmo espírito do `AddCongregaPayments(isDevelopment)` — em produção,
      onde o app roda em HTTPS, continua obrigatório.
      **(2) `session.tsx` descartava a sessão renovada** — a hidratação chamava
      `apiClient.request('/auth/refresh', {anonymous:true})` direto e lia
      `apiClient.session` depois, mas `request()` sozinho nunca chama
      `#adoptSession()` — só o método privado `#refresh()` faz isso. O servidor
      respondia **200 com sessão válida** e o app concluía "anônimo" mesmo
      assim, silenciosamente: o sintoma é indistinguível de "não tem sessão
      mesmo". Corrigido com `ApiClient.hydrateSession()`, novo método público
      que passa pelo `#ensureFreshSession()` já existente — reaproveita a
      coordenação de voo único em vez de abrir uma segunda renovação
      concorrente, que o refresh por rotação leria como reuso. 3 testes novos
      em `client.test.ts`.
      Sem o primeiro bug, o segundo nunca teria aparecido no teste manual — o
      cookie ausente já derrubava a sessão antes de chegar ao código que a
      descartava de qualquer forma. **Verificado pela UI real**: login, reload,
      navegação entre 6 rotas autenticadas, sessão mantida em todas.
- [x] Storage de token divergindo por plataforma
- [x] Fluxo de autenticação: entrar, código, início
- [x] Marca desenhada e ícone do app em todos os tamanhos
- [x] **Painel de início com dados reais** — contagem de membros e
      aniversariantes do mês (`useDashboard`), não mais um aviso de "em
      construção"
- [x] Estados de carregamento, erro e vazio na lista de membros
- [x] **Padronizar carregando/erro/vazio nas demais listagens** — `AsyncContent`
      (`@congrega/ui`) decide entre as quatro situações; as oito telas que
      repetiam a tríade à mão passaram a usá-lo. O vazio continua sendo escrito
      por cada tela e passado pronto: "nenhum membro cadastrado" e "ninguém faz
      aniversário este mês" pedem texto e ação próprios, e um componente que os
      gerasse produziria um vazio genérico em todas. O que se padroniza é
      **quando** mostrar, não o quê.
      **Dois "tentar de novo" que não funcionavam, achados ao extrair:**
      (1) **na lista de membros o botão não fazia nada** — o `onPress` era
      `setBusca((b) => b)`, e o React descarta atualização de estado idêntico
      por `Object.is`, então o efeito nunca reexecutava. Parecia funcionar, o
      que é pior do que não existir: quem caía num erro de rede clicava, nada
      acontecia, e a conclusão razoável era que o app estava quebrado.
      (2) **em aniversariantes o erro não oferecia saída nenhuma** — e não por
      esquecimento de quem escreveu a tela: `useAniversariantes` não expunha
      recarga alguma. Os dois hooks ganharam `recarregar`.
      Isso é o que a skill de design chama de tratar falha como direção e não
      como humor — um erro sem caminho é um beco. O tipo agora expõe a escolha:
      é preciso **omitir** `onRetry` para produzir um beco.
      Achados menores no caminho: um comentário `//` que era válido dentro do
      ternário virou filho de JSX na migração e apareceria como texto na tela
      (convertido para `{/* */}`); o `fechamento` perdeu o estreitamento de tipo
      ao atravessar a fronteira do componente, pego pelo `tsc`; e seis arquivos
      ficaram com import morto de `ActivityIndicator`.
      Verificado ao vivo: **erro real provocado interceptando a rota** de
      membros no navegador, tela mostra o erro, clique em "Tentar de novo"
      restaura a lista com os 8 membros. As oito telas migradas conferidas com
      asserção do título de cada uma — a primeira rodada dessa checagem usava
      heurística frouxa e passou verde para a tela de **login**, porque a sessão
      caiu por rate limit no meio do laço; um teste que aprova a página errada é
      pior que nenhum, e por isso a asserção passou a ser estrita.
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
| `dotnet test` | 277 testes (164 domínio + 76 aplicação + 37 integração, todos os de integração com Testcontainers) |
| `npm run typecheck` | 4 pacotes limpos |
| `npm run test` | 123 testes |
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
| Idempotência de pagamento | verificada no domínio **e contra a API real**: mesma `Idempotency-Key` devolve o mesmo `paymentId` e grava uma linha só |
| Checkout do Congrega+ | verificado contra PostgreSQL real: preço do banco, chave por titular, plano B2B recusado, 401 sem token |
| Webhook em HTTP | verificado contra a API real: 202 válido, 200 reentrega, 400 adulterado/sem assinatura/replay; inválidos gravados como evidência |
| Processamento de webhook | **verificado ao vivo**: webhook assinado processado pelo `WebhookDispatcherService` real contra Postgres real; assinatura inválida nunca reivindicada; `SKIP LOCKED` provado com Testcontainers segurando lock em outra transação. Concessão de entitlement coberta por 9 testes de unidade (fake de gateway — dois processos de dev não compartilham cobrança simulada, ver TODO acima) |
| Cancelamento e histórico de pagamentos | **verificados ao vivo pela UI real**: cancelar mantém o acesso até o fim do período, sobrevive a reload (era onde estava o bug do paywall), 409 no cancelamento repetido. Máquina de estados fixada por 15 testes de domínio novos; filtro do repositório por 6 de integração, provados como regressão real |
| Telas de assinatura Congrega+ | **verificadas ao vivo pela UI real** (Playwright): status com assinatura ativa, vitrine sem assinatura, checkout de verdade a partir do clique até o código PIX exibido, item de navegação na sidebar — achou e corrigiu dois bugs reais (CORS sem `Idempotency-Key`, 500 em conflito de assinatura) que só apareciam pelo navegador |
| Criptografia de dado de criança | **critério do ADR-014 verificado literalmente**: `SELECT` cru na coluna de alergia devolve bytes ilegíveis; nonce aleatório provado (mesmo texto → bytes diferentes); adulteração lança em vez de devolver lixo. API **recusa subir** sem a chave |
| Schema do check-in infantil | 5 tabelas com RLS aplicadas ao banco real; isolamento cross-tenant, uso único por evento, idempotência da fila offline e o `CHECK` de "quem retirou" verificados com Testcontainers |
| Estados de carregando/erro/vazio | **padronizados em `AsyncContent`** nas 8 listagens; dois "tentar de novo" quebrados corrigidos (um era no-op, outro não existia). Recuperação verificada ao vivo interceptando a rota para forçar erro real |
| Telas do design system Perk | **verificadas em tela** — login, painel, membros, financeiro, agenda, lançamento capturados via Chromium/Playwright contra a stack real |
| Sessão web sobrevive a reload | **verificado pela UI real**: login, F5/navegação, continua autenticado — dois bugs corrigidos nesta rodada (ver abaixo) |
| Stack completa (Postgres + API + Workers + app web) | subida e exercitada junta nesta sessão: Outbox drena OTP novo em segundos, login ponta a ponta pela UI |
| CI (`.github/workflows/ci.yml`) | **escrito, nunca executado** — comandos validados um a um localmente |
| Acento verde (troca do lima) | **verificado ao vivo nas 5 telas** — login, início, membros, financeiro, agenda e assinatura renderizam sem crash após a troca global de tokens. Contrastes recalculados e fixados em 29 testes (`tokens.test.ts`); a inversão de `textOnAccent` (tinta → branco) está coberta por asserção que falha se alguém reverter. Decisão registrada como D9 em `docs/07-design-system.md` |
| Tipo de evento | **verificado ponta a ponta**: coluna `events.event_type` com `CHECK (1..5)` e default 5 aplicada ao banco real; evento criado pelo formulário com tipo "Ensaio" gravou, voltou na listagem com o ícone certo e mudou o resumo de "3 Outros" para "1 Ensaio · 3 Outros". Os 3 eventos anteriores ficaram em `Outro`, sem inventar classificação para dado antigo |
| Nome do usuário na sessão | **corrigido ao subir a stack**: `SessionResponse` nunca carregava `FullName` — o domínio tinha, o DTO não. A sidebar quebrava a aplicação inteira ao tentar iniciais de `undefined`. Corrigido no contrato HTTP **e** na borda do cliente (`?? null`), porque o tipo do TypeScript promete um campo que só o servidor pode garantir |
| Agenda idêntica ao mockup | **verificada em 1280px e 390px**: cartão único com fio recuado, bloco de data por dia, navegador de mês em faixa, sino inerte e nome no cabeçalho. Em 390px nada estoura (`scrollWidth == 390`) e o ícone de tipo fica **na mesma linha** do título — provado medindo as caixas do glifo e do texto, não por `isVisible()` |
| CRUD de tipo de evento | **verificado ao vivo pela UI real**: cadastrar, renomear, trocar ícone, desativar/reativar e excluir. O enum de cinco valores virou a tabela `event_types` por igreja — a migration transferiu os eventos já classificados **antes** do `DROP COLUMN`, e "Outro" virou `NULL`, que é o que ele significava. Provado no banco: nenhum evento perdeu classificação |
| Tipo em uso não pode ser apagado | **verificado ao vivo**: `DELETE` de um tipo com eventos responde **409** vindo da FK `RESTRICT`, com a mensagem que aponta para desativar. Quem recusa é a constraint, não a contagem — a contagem só decide se o botão aparece. Desativado sai do formulário de evento (verificado: 0 opções no dropdown) e continua na tela de administração, senão nunca poderia ser reativado |
| Nome de tipo duplicado | **verificado ao vivo**: 409 "Já existe um tipo chamado…" vindo do índice único `uq_event_types_tenant_nome`, não de um `SELECT` antes do `INSERT` |
| `Dropdown` no design system | componente novo — `Modal` em vez de painel absoluto, porque em RN não há `z-index` confiável entre árvores e o painel ficaria recortado pelo `ScrollView` do formulário. Opções são `radio` com `checked`, e a seleção tem marca ✓ além do fundo (WCAG 1.4.1) |
| Endereço como entidade | **verificado ao vivo**: `addresses` (do tenant, com RLS) e `postal_codes` (global, cache da ViaCEP). A migration transferiu os 6 campos inline de `members` para linhas **antes** do `DROP COLUMN`, ligando por coluna temporária com o `members.id` — casar por conteúdo faria dois membros da mesma família disputarem a mesma linha. Conferido no banco: 3 endereços migrados, 0 órfãos. Revisa a decisão registrada em `db/002_members.sql`, que só valia enquanto o endereço fosse exclusivo do membro |
| Busca de CEP | **cadeia inteira verificada ao vivo**: 1ª consulta a `01001000` responde `source: viacep`, grava no banco, e a 2ª responde `source: cache`. O campo `source` existe justamente para isso — sem ele, um cache quebrado seria indistinguível de um funcionando. CEP inexistente → 404 com "preencha manualmente"; CEP incompleto → 400 antes de sair pela rede. Persiste só os 5 campos pedidos: conferido contra `information_schema` |
| ViaCEP fora do ar não derruba cadastro | timeout de 5s (não os 100s padrão do `HttpClient`) e **toda** falha vira `null` → preenchimento manual. Só o `CancellationToken` do próprio request escapa, porque insistir por quem desistiu é trabalho jogado fora |
| Casa × apartamento | **verificado ao vivo e no banco**: o campo Andar só aparece com Apartamento, o rótulo do número vira "Número do apto", e um `INSERT` cru com andar em casa é **recusado** por `ck_addresses_andar`. A regra é assimétrica de propósito — casa nunca tem andar, apartamento pode ter — para não obrigar quem digita "Apto 32" de uma lista de papel a inventar um andar |
| `syncedValue` no `TextField` | o campo continua **não controlado** (a decisão original, por causa do cursor saltando em aparelho modesto). A busca de CEP escreve pelo mesmo caminho que a máscara já usava — `node.value` no web, `setNativeProps` no nativo — e só quando o valor difere do que o campo tem |
| Global Query Filter de `EventType` | **falha encontrada e corrigida**: a entidade nasceu sem filtro na rodada anterior. O RLS cobria, mas a defesa em profundidade do ADR-006 não é opcional. `Address` nasceu com filtro; `PostalCode` não tem, e a ausência está documentada — é tabela de referência global, como `roles` |
| Sistema Grove (3ª troca de design) | **verificado ao vivo em 1440/900/390px**: barra superior no lugar da sidebar, painel com herói + 3 métricas + 2 painéis + atalhos + rodapé, fiel ao mockup enviado. Nenhuma largura tem rolagem horizontal; as 5 telas renderizam sem crash. Decisão registrada como D10 em `docs/07-design-system.md` |
| Contraste do mockup | **18 pares reprovavam 4,5:1 e foram medidos um a um**, não estimados: o verde de ação (`#4c8b22`, 4,18 — falhava como texto **e** como fundo de texto branco), o âmbar (3,45), o rótulo de seção de 9px (2,66) e **oito** cinzas de apoio entre 2,45 e 3,90 que viraram um só. Cada correção é o tom do mockup escurecido pelo mínimo; 28 testes falham se alguém reverter |
| Verde e âmbar são indistinguíveis sem matiz | achado pelo próprio teste: 1,01:1 de luminância entre os dois. Não é corrigível sem destruir o desenho, então virou **restrição registrada** — há uma asserção que a mantém visível, e enquanto ela passar, categoria nunca é comunicada só por cor. Todo cartão categórico carrega ícone e rótulo escrito |
| Pesos tipográficos | Inter 600/700/800 acrescentados ao carregamento. A hierarquia do Grove é feita por **peso**, não por tamanho — sem carregar, tudo cai no peso mais próximo e a hierarquia some **sem nenhum erro aparecer** |
| Painel sem dado inventado | o mockup traz "↑ 12,5% vs. mês anterior"; **não foi implementado** porque nada guarda a contagem de membros de meses passados. O que ficou sai de dados reais: próximo aniversário (do mês corrente), divisão dos eventos por tipo, janela de 7 dias, e o status Hoje/Celebrado/Em breve por comparação de data |
| `useDashboard` alinhado ao padrão | passou de `erro: string` para `Failure`, como os outros seis hooks — o que lhe deu o botão "tentar de novo" que ele não tinha. Ganhou também os próximos eventos, numa terceira consulta em paralelo |
| Tela de membros no Grove | **verificada ao vivo em 1440 e 390px**: herói com ações Importar/Cadastrar, 3 cartões de resumo, alternador Lista/Famílias, busca, chips de filtro e linhas com avatar, selo de aniversário, contatos e situação. Fiel ao mockup enviado; sem rolagem horizontal em nenhuma largura |
| Chips de filtro contam o ACERVO, não a página | o mockup pede "Perfil incompleto **5**", "Sem telefone **3**" — números que a API não tinha. Em vez de contar os 30 itens carregados (que diria 5 numa igreja com 12), foram implementados de verdade: `GET /members/summary` com **uma** consulta agregada, e `?gap=` na listagem. **Verificado contra o banco**: os 4 números da API batem com o SQL direto, e cada filtro devolve exatamente o que o chip promete (11, 11, 12) |
| Vazio conta como ausente, não só nulo | importação de planilha com célula em branco grava string vazia; um filtro só de `IS NULL` diria que a pessoa tem telefone e deixaria de fora justamente quem a secretaria precisa cobrar |
| Onde a tela diverge do mockup | **paginação por "carregar mais"**, não por números de página: a lista acumula, e páginas numeradas fariam quem rolou até o fim voltar ao topo. **A situação de vínculo (Inativo/Transferido/Falecido) aparece como selo** além do "Incompleto" — o mockup só tem completude, e um membro falecido passando sem marca esconderia o que importa |
| Tela de cadastro de membro no Grove | **verificada ao vivo em 1280 e 390px**: trilha, cabeçalho com "Voltar", dois painéis com ícone (Dados pessoais / Endereço), resumo lateral com avatar de iniciais, lista de conferência, barra de progresso, cartão de dica e barra de ações fixa com frase contextual. Fiel ao mockup; sem rolagem horizontal em nenhuma largura |
| Tipo de residência: 2 → 4 valores | o mockup pede Casa, Apartamento, Sítio/Chácara e Outro; o domínio só tinha dois, com `CHECK (1..2)`. Um select oferecendo opções que a API recusa seria pior que a divergência, então foi estendido de verdade: enum, `CHECK (1..4)` e migration aplicada |
| A regra do andar precisou ser INVERTIDA | era negativa (`residence_type <> 1 OR andar IS NULL` — "casa não tem andar"), o que cobria tudo com dois valores e passaria a **permitir** andar em sítio e "outro". Virou positiva (`residence_type = 2 OR andar IS NULL`). **Verificado**: `INSERT` cru de sítio com andar é recusado por `ck_addresses_andar`, e 5 testes de domínio novos cobrem os quatro tipos |
| Campos de estado sem virar controlados | o resumo lateral acompanha a digitação, o que exigiu espelhar os campos em estado. O `TextField` continua **não controlado** — nada é escrito de volta no input, então o cursor não salta. O anti-padrão que o componente evita é `value={...}`, não "estado que espelha o input" |
| Resumo sem dado inventado | o mockup mostra idade e cidade na prévia; aqui a prévia mostra a cidade **quando digitada** e, sem ela, diz o que a prévia é. Progresso, selo de nascimento e frase da barra saem do que foi preenchido |
| Lançamento financeiro detalhado | **verificado ao vivo e contra o banco**: título, tipo próprio, conta, recorrência e cor de categoria. O resumo bate com o SQL direto (3/2/1) e cada filtro devolve o que o chip promete. Migration transferiu o sinal de `giving_categories.kind` para `giving_entries.kind` **antes** do `NOT NULL` — 0 divergências em 3 lançamentos existentes |
| O sinal do dinheiro mudou de dono | o `db/006_financeiro.sql` registrava "o sinal vem de `giving_categories.kind`". **O que invalidou foi a categoria poder ser `Ambos`** — uma categoria que serve a entrada e saída não tem sinal para emprestar. O sinal desceu para o lançamento; a categoria virou **restrição**. Continua sendo uma verdade só, em outra coluna. Verificado: saída em categoria de entrada responde 400; em categoria `Ambos`, 201 |
| Badge Entrada/Saída em 100% das linhas | era bug de UI, não falta de coluna: `lancamento.kind` já vinha em toda linha, e a tela renderizava o selo só quando `isSaida` — a ausência tinha de ser lida como "entrada" |
| Forma de pagamento estendida | já era enum (não texto livre). Ganhou `CartaoCredito`, `CartaoDebito` e `Boleto`. **`Cartao` ficou**: os lançamentos gravados com ele não dizem se era crédito ou débito, e escolher um seria inventar informação financeira. O formulário não o oferece; a listagem o desenha |
| Recorrência marca, não gera | a coluna registra que a despesa se repete. **A geração automática não existe** e é fatia própria: exige worker com janela, idempotência por período e regra para edição da série — qualquer uma errada duplica dinheiro no relatório |
| `unaccent` na semeadura de categoria | o índice único é por `lower(name)` e não ignora acento, então a semeadura criou "Dízimo" ao lado do "Dizimo" existente. Encontrado ao conferir o banco depois de aplicar; corrigido com `NOT EXISTS` acento-insensível e o duplicado removido do banco de desenvolvimento |
| Anexo de comprovante | **NÃO implementado.** Depende de "Fornecedor de mídia", decisão pendente declarada — a mesma que segura a foto da criança no ADR-014. Guardar binário em coluna não escala, e escolher um bucket sem a decisão criaria migração de storage depois |
| Lançamentos periódicos | **verificado ao vivo**: uma série mensal cria o original + **12 parcelas** numa transação só. `generatedCount` volta na resposta para a tela poder dizer o que criou. Conferido no banco: 13 linhas na série, 12 previstas |
| Previsto ≠ realizado | **a geração colidia com uma regra do domínio**: `Register` recusa data futura, porque "um lançamento de 2027 sairia silenciosamente do fechamento". Gerar 12 meses à frente faria o caixa de setembro afirmar que o aluguel de setembro já foi pago. Resolvido com o par da contabilidade — previsto/realizado. **Verificado**: a parcela de setembro aparece na listagem, marcada, e o fechamento de setembro soma R$ 0,00 |
| Fim de mês na série | **bug pego pelo próprio teste**: eu avançava a partir da parcela anterior, então 31/jan encolhia para 28/fev e a série inteira deslizava para o dia 28 para sempre. Corrigido calculando cada parcela a partir da data ORIGINAL. Verificado ao vivo: 31/jul → 31/ago → 30/set → 31/out → 28/fev |
| Fechamento somava pelo lado errado | **bug que eu introduzi na rodada anterior e não peguei**: o fechamento agrupava por categoria e lia o sinal dela, então uma categoria `Ambos` produzia uma linha que não entrava nem em receita nem em despesa. **R$ 84,00 de saídas estavam invisíveis** no fechamento de agosto. Corrigido agrupando por (categoria, tipo do lançamento); verificado contra o SQL direto: 128400 = 128400 |
| Série não duplica | índice único parcial `(series_id, occurred_on) WHERE series_id IS NOT NULL`. Clique duplo ou retry de rede é recusado pela constraint em vez de gravar uma segunda parcela do mesmo mês — em livro-caixa, parcela duplicada é dinheiro que não existe |
| Tela do financeiro no Grove | **verificada ao vivo em 1440/900/390px**: herói com navegador de mês, 3 cartões de total com nota, barra de ações, busca, chips, painel de lançamentos com ícone de direção e valor com sinal, e painel "Resumo do mês" com barra e percentual por categoria. Fiel ao mockup; sem rolagem horizontal em nenhuma largura |
| Exportar CSV | o mockup traz o botão e ele **não existia**. Implementado de verdade: varre **todas as páginas** do mês (não só as 100 carregadas — exportar 100 de 130 e chamar de "lançamentos de agosto" é prestação de contas incompleta que ninguém percebe olhando o arquivo). Separador `;` e BOM UTF-8 por causa do Excel pt-BR. **Só no navegador**: baixar arquivo em iOS/Android exige outro fluxo, e botão que não faz nada ensina a desconfiar dos outros. Verificado: download real, 6 linhas para 6 lançamentos, BOM presente |
| Percentual é dentro do próprio tipo | "54% das entradas", não "54% do mês". Misturar entrada e saída num denominador só produziria um número sem significado — R$ 100 de dízimo não são fração de R$ 1.200 de aluguel |
| Cartão e chip contam coisas diferentes | **inconsistência encontrada na captura**: o cartão dizia "4 lançamentos" ao lado de um valor que continha 3, porque a contagem vinha do resumo (todos os estados) e o valor do fechamento (só realizado). A contagem passou a sair do **mesmo objeto** que o valor, e a tela explica a diferença quando há previsto no mês. Verificado ao vivo: cartão=3, chip=4 |

## Documento fiscal, detalhe do lançamento e cofre — verificados rodando

| Item | Estado |
|---|---|
| Documento fiscal em saída | **verificado ao vivo**: painel aparece só quando o tipo é Saída, começa em "Sem documento", e os campos (tipo, número, série, CNPJ/CPF, chave) só surgem em "Possui" |
| "Documento só em saída" é constraint | FK composta `(entry_id, entry_kind) → giving_entries (id, kind)` + `CHECK (entry_kind = 2)`. **Provado contra o Postgres**: o `INSERT` numa entrada é recusado pelo banco, não por um `if` |
| Anexo em Base64 | o contrato recebe e devolve Base64 — JSON não carrega binário. **O banco guarda BYTEA**: persistir o texto custaria 33% a mais de disco por comprovante e um decode a cada leitura. Verificado: o PNG volta byte a byte idêntico ao que subiu |
| `CHECK (octet_length(content) = size_bytes)` | sem ele, declarar `size_bytes = 1` passaria pelo teto de 10 MB e gravaria o arquivo inteiro — o CHECK de tamanho protegeria um número, não o arquivo. **Provado**: o banco recusa o tamanho mentido |
| Dígito verificador de CPF/CNPJ | conferido no domínio, incluindo a rejeição de "111.111.111-11" e afins, que **passam** na fórmula e são o buraco clássico. Um dígito trocado ao copiar do papel produziria um fornecedor inexistente, descoberto só na conferência anual |
| Detalhe do lançamento | **verificado**: ficha completa com autor e data de registro, documento fiscal com CNPJ formatado, anexo com nome e tamanho, e "Ver comprovante" abrindo o arquivo real via `blob:` |
| Anexar depois | endpoint próprio, e não um bloco no `POST /entries`: 13 MB de Base64 viajando junto fariam uma queda de conexão perder também o lançamento. E **a nota costuma chegar dias depois** do pagamento — o detalhe permite anexá-la sem relançar a despesa |
| Cofre | **verificado ao vivo**: saldo, extrato com saldo resultante por linha, autoria e depósito/retirada funcionando (R$ 3.620 → R$ 3.740) |
| Cofre não é entrada nem saída | tabela própria, fora do fechamento. **Provado**: depositar R$ 5.000 no cofre não moveu as saídas do mês. Se fosse lançamento, guardar dinheiro apareceria no relatório como gastá-lo |
| Saldo do cofre não fica negativo | `CHECK (balance_after_cents >= 0)` + `UNIQUE (tenant_id, sequence_number)`. **Provado**: as duas constraints recusam. O índice é o que serializa dois saques simultâneos — um `if (saldo >= valor)` aprovaria os dois contra a mesma leitura |
| Permissão `giving.vault` | criada agora porque os perfis vêm depois: restringir vira **tirar uma linha de `role_permissions`**. Só `Treasurer` recebe — `ChurchAdmin` tem `giving.read` e não `giving.write`, e dar-lhe o cofre entregaria mais poder sobre o dinheiro físico do que ele tem sobre o livro |
| **Bug de fuso encontrado e corrigido** | `formatDate('2026-08-22')` virava meia-noite UTC e, em São Paulo, exibia **21/08**. Dez telas contornavam colando `T12:00:00Z` à mão; o detalhe novo não. Corrigido em `toDate`, com 4 testes — inclusive o caso que dói: o dia 1º aparecendo no mês anterior, e portanto no fechamento errado |

### Aberto

| Item | Por quê |
|---|---|
| Anexo no celular | escrito e tipado, **não executado**: não há dispositivo nesta máquina. O caminho web foi verificado ponta a ponta com arquivo real |
| Trocar um documento fiscal já anexado | hoje o segundo é recusado com 409 (`UNIQUE (entry_id)`) — dois comprovantes na mesma despesa é o começo de uma despesa prestada em dobro. Corrigir um documento errado exige apagar e refazer o lançamento |
| Perfis com acesso restrito ao cofre | a permissão existe e está concedida só ao tesoureiro; falta a tela que administra papéis |

## Tela de configurações e conectores — verificada rodando

| Item | Estado |
|---|---|
| Tela `/configuracoes` | **verificada ao vivo em 1440/900/390px**: os três conectores (E-mail, Telegram, Google Drive), com quatro estados distintos — não configurado, desligado, nunca testado, último teste falhou. "Nunca testado" e "falhou" são coisas diferentes, e só a segunda exige ação agora |
| Credencial cifrada em repouso | AES-256-GCM com **chave própria** (`Connectors:DataKey`), separada da do check-in infantil: rotacionar uma não pode invalidar a outra. **Provado por SQL**: a senha não aparece nos bytes, e o tamanho é exatamente `conteúdo + 28` (nonce 12 + tag 16) |
| `CHECK (octet_length(secret_enc) >= 29)` | alarme contra alguém gravar senha em claro contornando o cifrador — texto cifrado real nunca é menor que nonce + tag |
| O segredo nunca sai da API | o contrato **não tem campo de leitura** para ele: nem cifrado, nem mascarado, nem "últimos quatro". Só `hasSecret`. Verificado no JSON de resposta |
| Editar sem redigitar a senha | **preserva a credencial** — verificado: 54 bytes antes, 54 depois, com o remetente alterado. Sem isso, corrigir a porta apagaria a senha, e a causa seria o campo que ninguém tocou |
| "Testar conexão" fala com o serviço | não simula: o SMTP **envia um e-mail de verdade** (conectar e autenticar não prova relay), o Telegram manda mensagem no chat configurado, o Drive assina um JWT e lê a pasta. O resultado é **persistido**, porque a pergunta que importa dias depois é "isto chegou a funcionar?" |
| Mensagens de erro orientam | "O servidor não oferece STARTTLS nesta porta. Tente 587 com StartTls, ou 465 com SslOnConnect" em vez de repetir um código SMTP. Verificado contra o Mailpit |
| Sem opção de SMTP em claro | e sem "ignorar erro de certificado". As duas destravariam servidores mal configurados e transformariam o TLS em teatro — a senha da conta de e-mail da igreja costuma ser a que recupera as outras senhas dela |
| Permissão `connectors.manage` | vale para **ler** também: a configuração revela para onde a igreja manda dados e com qual conta. Só `ChurchAdmin` |
| Mailpit no compose (perfil `dev`) | servidor SMTP local para exercer o conector sem mandar mensagem a ninguém |
| **Dois bugs de composição encontrados rodando** | os testadores ficaram num método que a **API** não chamava — o endpoint respondia "sem teste disponível" para uma integração que sabe se testar. Depois o protetor de segredo ficou num método que o **worker** não chamava, e ali o estrago seria maior: o envio não resolveria e **todo código de login iria para dead letter**. Os dois foram reunidos em `AddCongregaConnectors`, chamado explicitamente pelos dois processos |
| Dispatcher do Outbox | **rodou pela primeira vez nesta verificação**: 9 mensagens reivindicadas, 9 processadas, 0 em dead letter |

### Aberto

| Item | Por quê |
|---|---|
| Envio bem-sucedido por SMTP real | **não executado**: o Mailpit local não oferece TLS, e o código recusa conexão sem ele — corretamente. Falta uma credencial de provedor de verdade (senha de aplicativo do Gmail) para fechar o caminho ponta a ponta |
| Teste do Telegram e do Drive contra o serviço real | escritos e compilando, **não executados**: exigem token de bot e JSON de conta de serviço reais |
| Consumidor para Telegram e Drive | as credenciais são guardadas e testáveis, e **nada as usa ainda**. A tela diz isso em cada card em vez de apresentar os três como equivalentes |
| E-mail de login por conta da igreja | **não é possível por construção**: o OTP é enviado antes de haver igreja selecionada — `users` não tem `tenant_id`. Precisa de um remetente de plataforma, configurado fora do banco |

## Seletor de mês e ano — verificado rodando

| Item | Estado |
|---|---|
| Salto direto para qualquer mês | **verificado ao vivo**: um clique no ano e um no mês chegam a março do ano passado, e os dados daquele mês carregam. Antes eram 17 toques na seta |
| O ícone de calendário deixou de ser enfeite | ele sempre pareceu um botão e não era nada. Agora o período inteiro — ícone, rótulo e chevron — é o gatilho: um alvo de 16px é difícil de acertar no celular, e o rótulo ao lado já é para onde o olho vai |
| `onSelect` é obrigatório no componente | e não opcional. É o que impede o ícone de voltar a ser decorativo por descuido em uma tela nova |
| Piso de 10 anos, teto de 2 | **verificado: para em 2016 e a seta fica `aria-disabled`**, não inerte. Sem limite dá para chegar a 1998 e concluir que o sistema perdeu os lançamentos — um mês vazio é indistinguível de um mês perdido. O teto de +2 não é folga: uma série criada em dezembro gera parcelas **previstas** até dezembro do ano seguinte, e elas precisam ser alcançáveis |
| Sair do piso reabilita a seta | verificado — senão o limite vira armadilha |
| Reabrir mostra onde estou | e não o ano que eu estava folheando quando desisti. Sem isso, quem abre, vai até 2021, fecha e reabre é recebido por 2021 e conclui que navegou sem querer |
| "hoje" é marcado com palavra | e o mês escolhido, com preenchimento. Depois de navegar, "onde estou" e "onde é hoje" deixam de ser a mesma célula — dois estados pedem dois sinais, e um ponto colorido sozinho não serviria |
| Agenda e fechamento também | é o mesmo controle. Deixar um deles para trás faria o mesmo ícone ser botão numa tela e enfeite na outra |
| Alvo de toque | 114×44 no desktop, 108×44 em 390px — acima do mínimo, verificado nas três larguras |
| **Erro de medição no meu próprio teste** | a primeira asserção do piso lia `body.innerText` e capturava o "março de 2025" do cabeçalho **atrás** do modal: passou dizendo "parou em 2025" enquanto o seletor estava em 2016. Passar pelo motivo errado é pior do que falhar. Corrigida para ler de dentro do modal |

## Previsto visível e confirmável — a partir de um relatório de bug

O relato: setembro mostrava a saída recorrente de R$ 1.200 na lista e **R$ 0,00**
no cartão de SAÍDAS. O total estava certo — o aluguel de 30/09 não foi pago — mas
a tela não dizia isso, e investigar revelou uma lacuna maior.

| Item | Estado |
|---|---|
| **A lacuna real** | um lançamento `Previsto` **nunca podia virar realizado**: `GivingEntry.Confirmar()` existia e era testado, e não havia endpoint nem botão. As doze parcelas de toda série eram um beco sem saída — o aluguel seria pago no mundo real e continuaria valendo R$ 0,00 no caixa, para sempre |
| `POST /entries/{id}/confirm` | criado. Recusa data futura com 409 e o motivo escrito; confirmar o que já está realizado é **silencioso**, não erro — dois cliques chegam ao mesmo estado. **Verificado**: status vira 1 no banco, o valor entra no fechamento (251260 + 31337 = 282597) e a tela reflete |
| Botão "Confirmar" na linha e no detalhe | **só aparece quando a confirmação pode dar certo**. O domínio recusa confirmar o futuro, e oferecer o botão assim mesmo produziria um erro que a própria tela sugeriu. Verificado: mês futuro não mostra o botão; parcela de ontem mostra |
| O previsto ficou visível | `R$ 1.200,00 ainda previsto` sob o total, **nunca somado a ele**. Somar faria o fechamento afirmar um pagamento que não aconteceu; esconder faz a tela mostrar R$ 0,00 ao lado de uma lista com lançamentos — foi exatamente isso que pareceu defeito |
| "ainda previsto", e não "+ R$ 1.200" | o sinal de mais convidaria a somar de cabeça com o número de cima, que é o que este campo existe para impedir |
| Comparação de data por texto ISO | e não por `Date`: ler `2026-09-30` como meia-noite UTC diria, em São Paulo, que o dia 30 ainda não chegou às 21h do próprio dia 30. É o mesmo deslize de fuso já corrigido em `formatDate` |
| Testes de domínio | 2 novos fixam o caso relatado: previsto não entra em `TotalExpenseCents` nem no saldo, **e** continua visível em `PlannedExpenseCents` |
| **Duas falhas no meu próprio teste** | ele parseava o command tag do `psql` junto do valor (`"120000\nUPDATE 1"` → `NaN`) e reprovou uma conta que estava certa. E movia uma **parcela real** da igreja para ontem — o que alterava a contabilidade de desenvolvimento do usuário e, na segunda execução, colidiu com `UNIQUE (series_id, occurred_on)`. Passou a criar um lançamento próprio, sem série, e apagá-lo no fim |

### Aberto

| Item | Por quê |
|---|---|
| Confirmar em lote | hoje é uma parcela por vez. Uma igreja que deixou três meses acumularem confirma três vezes |
| Desfazer uma confirmação | `Confirmar()` não tem inverso. Quem confirmar por engano precisa apagar e relançar |

## Carregamento de tela — `ScreenLoading`

| Item | Estado |
|---|---|
| Um componente, oito cópias a menos | o bloco `<Screen><ActivityIndicator /></Screen>` estava copiado em **seis telas** (as duas de detalhe de membro e evento, as duas de edição, a família e o lançamento) e o rodapé de "carregar mais" em outras duas. Todas com os mesmos dois defeitos |
| **O defeito grave era de acessibilidade** | um `ActivityIndicator` solto **não tem nome acessível**: quem usa leitor de tela chegava numa tela que não anunciava nada, ouvia silêncio e concluía que o aplicativo travou. A única informação disponível era visual — a única que essa pessoa não recebe. **Verificado**: `role="progressbar"` com `aria-label="Carregando a ficha do membro…"` presente **desde o primeiro instante**, antes mesmo de o indicador aparecer |
| Atraso de 250 ms antes de aparecer | uma resposta de 90 ms é percebida como instantânea, e um indicador que surge e some nessa janela é ruído puro — faz o layout saltar duas vezes em vez de uma. **Verificado nos dois sentidos**: com a API atrasada em 1,5 s o indicador aparece (opacidade 0 → 1); com a API em 60 ms ele **nunca** fica visível, medido em 12 amostras de 25 ms |
| Sem salto de layout | o bloco ocupa o espaço desde o início e só a opacidade muda — a entrada do indicador não empurra nada |
| Diz **o que** está carregando | "Carregando a ficha do membro…" em vez de um giro mudo. O texto entra também no nome acessível |
| Respeita `prefers-reduced-motion` | o giro some e fica o texto. O `ActivityIndicator` do React Native não tem como parar de girar; a saída é não desenhá-lo, e o texto sozinho diz tudo o que ele dizia |
| `AsyncContent` caiu no mesmo componente | o ramo sem esqueleto usava a própria cópia. Agora há uma implementação só |

### Aberto

| Item | Por quê |
|---|---|
| Rodapé de "carregar mais" | trocado nas duas listas e **não verificado rodando**: com 17 membros a lista cabe numa página e o rodapé nunca aparece. Forjar uma segunda página seria testar um cenário que este banco não tem. O que foi verificado é que as duas telas continuam renderizando |
| Teste de componente no `@congrega/ui` | o pacote só tem teste de lógica pura (`tokens.test.ts`); não há React Testing Library configurada. O atraso e o rótulo foram fixados por verificação ao vivo, não por teste automatizado |

## Agenda refatorada conforme o mockup

| Item | Estado |
|---|---|
| Estrutura do mockup | **verificada ao vivo em 1440/900/390px**: herói com subtítulo que conta os eventos do mês, barra de período com Tipos e Agendar, quatro cartões de resumo com ícone, painel com busca, chips de filtro por tipo, linhas com faixa colorida e etiqueta, paginação e rodapé |
| **Cor por tipo de evento** | `event_types.color_hex`, mesma coluna e mesmo CHECK de `giving_categories` — é o mesmo problema resolvido do mesmo jeito. A migration dá uma cor inicial aos tipos que já existiam: sem isso toda igreja abriria a agenda nova com faixas cinzas e concluiria que a cor não funciona, em vez de que ela ainda não foi escolhida |
| A cor é escolhida pela igreja | seletor na tela de tipos, **paleta fechada de seis**. Campo livre deixaria escolher amarelo-claro e o tipo sumiria do cartão branco; as seis foram medidas contra o branco e passam de 3:1 (1.4.11). **Verificado ponta a ponta**: trocar para Roxo grava `#7C5CBF` e a agenda passa a desenhar `rgba(124, 92, 191, 0.18)` |
| Cada cor tem nome | "Verde", "Azul", "Roxo"… — um seletor cujas opções só se distinguem por matiz é inacessível por definição, e "opção 3" não diz qual foi escolhida. O visto marca a ativa **além** da borda |
| A cor nunca informa sozinha | a faixa e o ponto do chip são decorativos e escondidos do leitor de tela; a etiqueta com o **nome do tipo** está sempre ao lado |
| Busca e filtro por tipo | no cliente, sobre o mês já carregado — algumas dezenas de linhas. Se a agenda um dia paginar dentro do mês, isto volta para o servidor: senão o filtro veria só um pedaço e diria "nenhum evento" sobre uma agenda cheia |
| Editar e apagar na linha | alvos **irmãos** do conteúdo, nunca aninhados: no navegador o clique sobe, e apagar abriria o detalhe do evento que acabou de sumir. **Verificado**: apagar removeu do banco, sumiu da tela e não navegou |
| Ações somem em 390px | competiriam com o título por uma largura que não existe, e o detalhe oferece as duas |
| **O que do mockup NÃO entrou** | o alternador Lista/Semana/Mês. Duas das três visões não existem, e um controle em que 2 de 3 opções não fazem nada é o mesmo defeito do ícone de calendário que virou botão nesta sessão |

### Aberto

| Item | Por quê |
|---|---|
| Visões de Semana e Mês | o alternador do mockup pressupõe as três. Cada uma é uma tela própria, com layout de grade e cálculo de semana |

## Eventos recorrentes semanais

| Item | Estado |
|---|---|
| "Todo domingo tem culto às 19h" | **verificado ao vivo**: 53 encontros criados, `dow = 0` em todos, `19:00` em todos, `02:00:00` de duração em todos. Um `series_id` só |
| Linhas de verdade, não regra expandida na leitura | a igreja precisa mexer numa ocorrência — o culto do dia 25 será no salão, o de 1º de novembro está cancelado. Com regra expandida, cada exceção vira uma tabela de exceções e a agenda deixa de ser um `SELECT`. Custo: 52 linhas por série anual, que é nada |
| **A soma acontece no relógio de parede** | e não em horas corridas. Somar 168h ao instante dá o mesmo resultado hoje e daria **uma hora de diferença** se o Brasil voltasse ao horário de verão: o culto das 19h cairia às 18h no meio da série, e ninguém olharia a agenda de agosto para descobrir isso em outubro. Fixado por teste |
| Cada data sai da ORIGINAL | e não da anterior. Semanal é menos suscetível que mensal — foi lá que o bug apareceu — mas escrever a regra de dois jeitos é que produz o terceiro |
| `UNIQUE (series_id, starts_at)` parcial | **provado contra o Postgres**: o banco recusa dois cultos no mesmo instante da série. Clique duplo e retry de rede não duplicam a agenda |
| Apagar pergunta o quê | "só este encontro" ou "a série inteira". Um apagar que remove só a ocorrência deixa 51 para trás; um que remove tudo apaga o ano sem avisar. **Verificado nos dois caminhos**: recusar o diálogo levou 53 → 52; aceitar levou 52 → 0 |
| Apagar a série existe desde o começo | gerar 52 linhas sem oferecer o caminho de volta seria a mesma armadilha das parcelas previstas do financeiro — um beco sem saída |
| O formulário avisa antes | "cerca de 52 encontros… cobrindo os próximos 12 meses", e que dá para apagar a série. Sem isso, quem marca "toda semana" descobre o que criou ao trocar de mês, tarde demais para ser um clique de arrependimento |
| Selo "Semanal" na linha | sem ele, apagar um culto e ver outros cinquenta iguais no lugar pareceria defeito |
| Só na criação | editar um evento nunca gera série: transformar um culto avulso em 52 exigiria decidir a partir de quando, e a resposta errada enche a agenda de linhas que ninguém pediu |

### Aberto

| Item | Por quê |
|---|---|
| Editar a série inteira | não existe, e é deliberado: nada guarda a definição da repetição, só a identidade. Mudar o horário de todos os cultos futuros exige apagar e recriar. Guardar a definição traria a pergunta que todo calendário erra — "esta, as futuras, ou todas?" — cuja resposta errada reescreve o passado da igreja |
| Frequência mensal | traria a ambiguidade "dia 5" *vs* "primeiro domingo", e a igreja que marca santa ceia no primeiro domingo não seria atendida por nenhuma das duas sem escolher qual |
| Mais de um dia por semana | "toda terça e quinta" são duas séries hoje. Um conjunto de dias é a evolução natural |

## Carregamento global — overlay bloqueante em todos os módulos

| Item | Estado |
|---|---|
| `GlobalLoadingProvider` na raiz | overlay em `Modal`, cartão com anel (trilho + arco girando + ponto pulsando), título, mensagem e barra indeterminada — a estrutura do exemplo |
| Ligado em **todos os módulos** | agenda (novo, editar, apagar, apagar série, tipos), financeiro (lançar, confirmar, apagar, anexar comprovante, exportar, cofre), membros (novo, editar, família), configurações (salvar, testar, remover). 24 pontos |
| Mensagem própria de cada operação | "Criando a série… agendando cerca de 52 encontros semanais", "Testando a conexão… pode levar até 20 segundos", "Anexando o comprovante… arquivos grandes levam alguns segundos". Um "Carregando…" genérico em 24 lugares não diria nada em nenhum |
| **A API é `executar`, não mostrar/esconder** | um par manual vaza no primeiro caminho de erro que alguém esquecer de cobrir — e o vazamento deixa a tela bloqueada para sempre, sem nada explicando, com recarregar como único caminho de volta. O `finally` mora no provider, uma vez. **Verificado**: forjando um 500, o overlay some |
| Contagem de operações em voo | duas operações simultâneas não fazem a primeira a terminar destravar a tela enquanto a outra ainda grava |
| **Sem atraso antes de aparecer** | ao contrário de `ScreenLoading`. Lá o atraso evita um piscar; aqui abriria uma janela em que a tela ainda aceita cliques — e um overlay que começa a bloquear 250 ms depois não bloqueia. **Verificado que bloqueia**: `elementFromPoint` sobre o botão "Lançar" devolve o overlay, não o botão |
| Movimento reduzido | anel parado em 45°, ponto sem pulsar, barra escondida, transição desligada — como o exemplo pede |
| A barra é decorativa e escondida do leitor de tela | ela não conhece o progresso real; anunciá-la como barra de progresso faria a tecnologia assistiva prometer uma porcentagem que não existe |
| Anunciado | `role="progressbar"` com título e mensagem no rótulo, e `accessibilityViewIsModal` para o leitor parar de percorrer o conteúdo atrás |

### A decisão que separa os dois carregamentos

O overlay é para **operação**, não para leitura. Salvar, apagar, enviar e testar
bloqueiam a tela — e o bloqueio é o ponto: um segundo clique em "Salvar" grava o
dízimo duas vezes. **Abrir uma lista continua com esqueleto**, que preserva o
layout e não rouba o controle; cobrir a tela para dizer "estou lendo" trocaria
uma espera informativa por uma espera cega.

### Aberto

| Item | Por quê |
|---|---|
| Aplicar o overlay às leituras | é uma linha por tela, se a preferência for consistência visual sobre o esqueleto. Não foi feito porque seria uma regressão de usabilidade nas listas |
