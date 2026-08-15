# Congrega — Sumário Executivo de Decisões (ADRs)

> Entregável 6.1. Cobre todos os itens da Seção 3 do briefing.
> Formato de cada ADR: contexto, decisão, alternativas descartadas, trade-off aceito, risco residual.
> As decisões de maior impacto (3.2, 3.3, 3.4, 3.5) recebem seção detalhada após a tabela.

---

## Tabela-síntese

| # | Decisão | Opção escolhida | Alternativas descartadas | Trade-off aceito | Risco residual |
|---|---|---|---|---|---|
| **001** | Modelo de isolamento multi-tenant | Linha compartilhada com `tenant_id` discriminador | Banco por tenant; schema por tenant | Isolamento lógico, não físico — um bug de aplicação pode vazar entre tenants | 🟡 Médio — mitigado por Global Query Filters + RLS (ADR-003) |
| **002** | Resolução do `tenant_id` | Claim no JWT, validada contra `memberships` a cada requisição | Subdomínio; header `X-Tenant-Id` | Troca de tenant exige novo token (ou claim de lista + seleção explícita) | 🟢 Baixo |
| **003** | Isolamento no EF Core | Global Query Filters + RLS no Postgres como rede de segurança | Só aplicação; só RLS | `SET LOCAL` por transação e uma role de banco a mais para operar | 🟢 Baixo |
| **004** | Usuários cross-tenant | Identidade global + `memberships` N:N; assinante sem igreja é usuário sem membership | `user` duplicado por tenant | Toda query de membro precisa do par (user, tenant), nunca só user | 🟢 Baixo |
| **005** | Autoridade de identidade | **API .NET é a única autoridade.** Supabase Auth desligado | Supabase Auth; identidade híbrida | Reimplementar OTP, refresh e revogação que o Supabase daria pronto | 🟡 Médio — código de auth próprio é superfície de ataque própria |
| **006** | Row Level Security | Habilitado nas tabelas tenant-scoped, como *defense in depth*; aplicação continua sendo a autoridade | RLS como autoridade única; sem RLS | Complexidade de propagar contexto; jobs precisam de role com `BYPASSRLS` | 🟡 Médio — `BYPASSRLS` mal usado anula o controle |
| **007** | Acesso direto do frontend ao Supabase | **Proibido.** Tudo passa pela API .NET | Storage direto com anon key; Realtime direto | Perde-se Realtime "de graça"; API vira caminho de todo download | 🟢 Baixo |
| **008** | Dono do schema | EF Core Migrations | Supabase CLI; migrations duplas | Objetos que o EF não modela (RLS policies, funções) vão em migration SQL manual versionada | 🟢 Baixo |
| **009** | Regras de loja (IAP) | Segmentação por natureza da venda: ChMS B2B via Abacate.pay; Congrega+ via IAP no iOS, Play Billing no Android, Abacate.pay na web | IAP para tudo; Abacate.pay para tudo | Margem 15–30% menor nas vendas B2C originadas em app; três integrações de billing | 🔴 **Alto** — regra de loja muda e é interpretada caso a caso |
| **010** | Armazenamento e entrega de mídia | Cloudflare R2 (egress zero) + provedor de vídeo com HLS assinado (Bunny/Mux) | Supabase Storage; S3 + CloudFront | Mais um fornecedor no stack; migração de assets se trocar | 🟡 Médio — custo de egress é o risco financeiro oculto |
| **011** | Proteção de download | URL assinada de TTL curto (≤ 300 s), emitida só após checagem de entitlement no backend | URL pública; link permanente | URL vaza dentro da janela do TTL | 🟡 Médio — nenhuma técnica impede captura por usuário autorizado |
| **012** | Base legal LGPD — dados de membro | Igreja = controladora, Congrega = operadora. Execução de contrato (Art. 7º, V) | Congrega como controladora | Contrato de operador obrigatório com cada tenant | 🟡 Médio — depende de validação jurídica (P4) |
| **013** | Convicção religiosa (Art. 11) | Consentimento específico e destacado, coletado pela igreja | Legítimo interesse; obrigação legal | Fluxo de consentimento a mais no cadastro de membro | 🟡 Médio — LGPD não tem carve-out explícito para entidade religiosa |
| **014** | Dados de criança (Art. 14) | Consentimento parental específico, com registro de prova; criptografia em coluna para alergias e foto | Tratar como dado pessoal comum | Complexidade de cripto em nível de aplicação | 🔴 **Alto** — maior dano reputacional possível do produto |
| **015** | Direito ao esquecimento | Anonimização de PII + preservação do ledger financeiro pseudonimizado | `DELETE` em cascata; soft delete simples | Registro contábil deixa de ter nome, mas continua auditável | 🟢 Baixo |
| **016** | Expo vs bare | **Expo com Dev Client**, Expo Router | Bare workflow; Expo Go puro | Dependência do ciclo de release do Expo para libs nativas | 🟢 Baixo |
| **017** | Monorepo | pnpm workspaces + Turborepo | Nx; polirepo | Curva de configuração inicial de cache e pipelines | 🟢 Baixo |
| **018** | Offline-first no check-in | Fila local SQLite + idempotency key + sincronização em background | Online-only; CRDT | Conflitos resolvidos por regra de negócio explícita, não automaticamente | 🟡 Médio — check-in duplicado se a chave não for estável |
| **019** | Idempotência de webhook | Tabela `payment_webhooks` com unique no `event_id` do provedor + persistir cru antes de processar | Checar "se já existe assinatura"; dedupe em memória | Uma escrita a mais por webhook | 🟢 Baixo |
| **020** | Estilo de arquitetura | **Monólito modular** com costuras explícitas por bounded context | Microsserviços; monólito sem módulos | Um deploy só; disciplina de fronteira depende de revisão e testes de arquitetura | 🟢 Baixo |
| **021** | Background jobs | `BackgroundService` nativo + advisory lock do PostgreSQL | Hangfire; Quartz.NET | Sem dashboard pronto de jobs; agendamento simples | 🟡 Médio — pooler em modo transaction quebra lock de sessão (P3) |
| **022** | CQRS | Apenas em contextos de leitura pesada (catálogo, relatórios). Escrita permanece no modelo de domínio | CQRS global; sem CQRS | Dois caminhos de leitura em partes do sistema | 🟢 Baixo |

---

## ADR-005 e ADR-006 — Supabase + EF Core: quem manda? (Seção 3.2)

Este é o ponto de atrito mais provável do projeto, e a skill `security-cloud-expert` (§35) é
categórica: *"Nunca combine mecanismos de segurança sem definir claramente qual camada é a
autoridade."*

### Decisão: a API .NET é a autoridade única de identidade

**Supabase Auth fica desligado.** Não há usuário no `auth.users` do Supabase. A tabela `users` do
schema da aplicação é a única fonte de verdade sobre quem existe.

**Por quê:** o briefing exige claims que o Supabase Auth não produz nativamente
(`tenant_id`, `subscription_tier`, `roles[]` derivados de `memberships`), exige rotação de refresh
token com detecção de reuso, e exige que a autorização considere *entitlement* — um conceito que
não é identidade. Manter Supabase Auth significaria sincronizar dois cadastros de usuário, e todo
sistema com dois cadastros de usuário eventualmente diverge. Duas fontes de verdade sobre
identidade é a definição de um incidente esperando data marcada.

**O que se perde:** OTP por e-mail, magic link, recuperação de conta e revogação de sessão que
viriam prontos. Isso é trabalho real, estimado no ADR de sequenciamento como Onda 1.

**Risco residual:** código de autenticação escrito em casa é superfície de ataque escrita em casa.
Mitigação: usar primitivas consolidadas (`Microsoft.AspNetCore.Authentication.JwtBearer`,
`System.Security.Cryptography`), nunca criptografia própria, e cobrir o fluxo com os testes de
segurança descritos no documento de autenticação.

### Decisão: RLS habilitado, mas como rede de segurança — não como autoridade

A hierarquia é explícita e vale como regra de revisão de código:

```
Autoridade    → Aplicação (.NET): policies de autorização + Global Query Filters do EF Core
Rede de       → PostgreSQL RLS nas tabelas tenant-scoped
segurança
```

**Por que não só aplicação:** o briefing faz a pergunta certa — *"o que acontece se um filtro for
esquecido?"*. A resposta honesta, sem RLS, é: **vaza**. Um `FromSqlRaw`, um `IgnoreQueryFilters()`
colocado para resolver um bug de relatório, uma entidade nova cadastrada no `DbContext` sem
filtro — qualquer um desses transforma um descuido de uma linha em vazamento cross-tenant. Global
Query Filter é opt-out silencioso por natureza, e é exatamente o tipo de controle que falha sem
alarme.

**Por que não só RLS:** RLS não conhece regra de negócio. Ele responde "esta linha pertence a este
tenant?", não "este usuário pode ver a ficha financeira deste membro?". Autorização de domínio
precisa estar no domínio.

**Como o contexto é propagado:** interceptor de conexão do EF Core
(`TenantConnectionInterceptor`) emite, ao abrir a conexão:

```sql
SELECT set_config('app.tenant_id', $1, false),
       set_config('app.user_id',   $2, false);
```

As policies leem `current_setting('app.tenant_id', true)::bigint`.

> ⚠️ **Revisão desta decisão durante a implementação.** A versão original deste ADR previa
> `SET LOCAL` por transação, escolhido para ser seguro sob o *transaction pooling* do Supavisor
> (P3). Ao implementar, o problema apareceu: **`SET LOCAL` só vale dentro de uma transação**, e
> leituras em autocommit — a maioria das queries de uma API — ficariam sem contexto, fazendo o
> RLS negar tudo. Forçar toda leitura a abrir transação explícita é caro e fácil de esquecer.
>
> **Decisão revisada:** a API conecta **direto** ao Postgres (porta 5432), usando o pool do
> próprio Npgsql, e o contexto vira GUC de **sessão**. Isso é seguro porque o Npgsql emite
> `DISCARD ALL` ao devolver a conexão ao pool, limpando os GUCs — o vazamento entre requisições,
> que era a razão de ser do `SET LOCAL`, não acontece.
>
> **Condição que invalida a revisão:** se a API passar a usar o Supavisor em *transaction mode*,
> é obrigatório voltar para `SET LOCAL` dentro de transação explícita. O `DISCARD ALL` do Npgsql
> não protege quando o multiplexador de conexões é externo ao processo. Esta armadilha não quebra
> nenhum teste e vaza dados entre tenants em produção — está anotada no código, em
> `TenantConnectionInterceptor`.

O uso de `NULLIF(current_setting(...), '')` nas policies garante **fail closed**: contexto ausente
vira `NULL`, a comparação resulta em falso e o acesso é negado, em vez de liberado.

**Duas roles de banco, com propósitos distintos:**

| Role | Uso | RLS |
|---|---|---|
| `congrega_app` | API, requisições de usuário | Aplicado |
| `congrega_worker` | Jobs que cruzam tenants legitimamente (retenção, faturamento) | `BYPASSRLS` |

**Risco residual:** `congrega_worker` é a chave-mestra. Se a API for configurada com ela por
engano, o RLS inteiro vira decoração. Controle: a credencial da role de worker só é injetada nos
deployments de worker; um teste de integração verifica que o pod da API não consegue ler linha de
outro tenant mesmo com o filtro do EF desabilitado.

### Decisão: frontend nunca fala com o Supabase diretamente

Nem Storage, nem Realtime, nem PostgREST. Motivo direto da skill de segurança (§35): *"a service
role key nunca deve chegar ao frontend"*, e a anon key só seria segura sob políticas RLS escritas
para um modelo de autenticação (o do Supabase Auth) que decidimos não usar. Sem Supabase Auth, o
JWT do frontend é nosso — o Postgres do Supabase não sabe validá-lo sem configuração adicional que
recriaria o acoplamento que estamos evitando.

Consequência prática: **todo download de conteúdo premium passa por um endpoint da API** que
valida o entitlement e devolve uma URL assinada (ADR-011). Isso é desejável, não um custo: é
exatamente onde a checagem de acesso precisa acontecer.

Realtime fica fora do MVP. Se surgir necessidade (por exemplo, painel de check-in ao vivo),
a opção preferida é SSE ou WebSocket servido pela própria API .NET.

### Decisão: EF Core Migrations é dono do schema

Uma única linha do tempo de migrations, versionada no repositório, aplicada por job de deploy.
O Supabase CLI não gerencia schema — o projeto Supabase é tratado como um Postgres qualquer.

Objetos que o EF Core não modela (policies de RLS, funções, índices parciais com expressão,
`CHECK` complexo) entram como `migrationBuilder.Sql()` dentro da mesma migration, para que schema
e políticas de segurança avancem juntos e nunca fiquem defasados entre ambientes.

---

## ADR-009 — Regras de loja: onde Abacate.pay é legal e onde IAP é obrigatório (Seção 3.3)

> ⚠️ **Premissa declarada:** as regras de Apple e Google mudam com frequência, variam por
> jurisdição e vêm sendo alteradas por decisões judiciais e regulatórias — inclusive no Brasil, via
> CADE. Não pude verificar o texto vigente das diretrizes nesta sessão. O desenho abaixo é
> deliberadamente **estruturado para sobreviver a mudanças**: a origem da assinatura é um dado, não
> uma bifurcação espalhada pelo código.

### O conflito

O briefing exige Abacate.pay. As lojas exigem IAP para conteúdo digital consumido no app. Não dá
para satisfazer os dois no mesmo fluxo — mas dá para segmentar por **natureza da venda**, que é
como as próprias diretrizes segmentam.

### Segmentação

| Fluxo | Quem paga | Onde ocorre | Meio | Justificativa |
|---|---|---|---|---|
| Assinatura do **ChMS** | A igreja (PJ) | Web, painel administrativo | **Abacate.pay** | Serviço B2B vendido a uma organização, não a um consumidor individual. Software de gestão, não conteúdo consumido no app |
| **Congrega+** contratado na web | Pessoa física | Navegador | **Abacate.pay** | Compra fora do app; margem integral |
| **Congrega+** contratado no app iOS | Pessoa física | App iOS | **IAP obrigatório** | Conteúdo digital desbloqueado dentro do app |
| **Congrega+** contratado no app Android | Pessoa física | App Android | **Google Play Billing** | Mesma lógica |
| **Pack avulso** comprado no app | Pessoa física | App | **IAP / Play Billing** | Compra de conteúdo digital |
| **Pack avulso** comprado na web | Pessoa física | Navegador | **Abacate.pay** | Fora do app |
| Conteúdo **já adquirido** na web | — | Qualquer app | **Consumo livre** | Acesso a conteúdo previamente comprado é permitido |

A última linha é o que torna a estratégia viável: o usuário que assinou pela web **usa o app
normalmente**, com tudo desbloqueado. O que não se pode fazer é vender dentro do app por fora do
IAP, nem — em storefronts onde o *anti-steering* ainda vigora — direcionar o usuário para a compra
externa a partir da tela do app.

### Comportamento do paywall por plataforma

O paywall não é um componente com um `if` no meio. É **um contrato, três implementações**, usando
a divergência por extensão de arquivo que a skill `react-native-expert` recomenda:

```
packages/ui/src/paywall/
  PaywallScreen.tsx          # layout, copy e telemetria — compartilhado
  useCheckout.ts             # contrato: startCheckout(planId) => Promise<CheckoutResult>
  useCheckout.ios.ts         # StoreKit via expo-in-app-purchases
  useCheckout.android.ts     # Play Billing
  useCheckout.web.ts         # redirect para checkout Abacate.pay
```

Regras que o código precisa respeitar, e que devem virar item de checklist de revisão:

- **iOS:** exibe preço vindo do StoreKit (nunca preço hardcoded — a loja é a fonte de verdade do
  preço local). Nenhum link, botão ou texto que direcione para compra externa.
- **Android:** Play Billing, mesma regra.
- **Web:** Abacate.pay, com preço vindo da API.
- **Preço:** o preço no app **pode ser maior** que na web para absorver a comissão. Isso é
  permitido; o que não é permitido é anunciar a alternativa mais barata dentro do app.
- **Aquisição:** a estratégia comercial deve empurrar a compra para a web por canais **externos**
  ao app — e-mail, WhatsApp, site, pregação institucional na própria igreja. Este é o principal
  lever de margem do produto B2C.

### Reconciliação no domínio: uma `Subscription`, várias origens

O domínio não sabe o que é Apple. Ele sabe que uma assinatura tem uma **origem** e um **estado**.

```csharp
public enum SubscriptionSource
{
    AbacatePay = 1,
    AppleAppStore = 2,
    GooglePlay = 3,
    Courtesy = 4   // cortesia concedida por administrador
}
```

Cada origem tem um adaptador que traduz seu evento nativo para o mesmo vocabulário de domínio:

```
Abacate.pay webhook  ─┐
App Store Server      ├─→  ISubscriptionSourceAdapter  ─→  SubscriptionStateChanged
  Notification V2     │                                          │
Google RTDN (Pub/Sub) ─┘                                          ▼
                                                        entitlements (acesso efetivo)
```

**Regra de ouro, direto da skill de segurança (§15):** *"Não trate 'pagamento aprovado' como
sinônimo universal de 'usuário premium'."* Pagamento aprovado gera evento; evento move a máquina de
estados; máquina de estados concede `entitlement`; **entitlement** é o que a autorização consulta.
Nunca se pergunta "esse usuário pagou?" — pergunta-se "esse usuário tem entitlement válido para
este recurso agora?".

Isso é o que permite que um usuário com assinatura Apple, um pack comprado na web via Abacate.pay e
uma cortesia dada pelo pastor tenham seus acessos resolvidos pela mesma consulta, sem nenhum `if`
por fornecedor no caminho de autorização.

**Validação de recibo é server-side, sempre.** O app envia o token/recibo; a API valida contra o
servidor da Apple/Google antes de conceder qualquer entitlement. Recibo validado no cliente é
recibo forjável.

**Risco residual (Alto):** uma mudança de política de loja, ou uma interpretação diferente por parte
de um revisor da App Store, pode invalidar o enquadramento B2B do ChMS. Mitigação: manter o app do
ChMS e o app do membro como **binários separados** — o app administrativo da igreja não vende nada
e não expõe conteúdo premium, o que reduz drasticamente a superfície de interpretação.

---

## ADR-010 e ADR-011 — Entrega e proteção de mídia (Seção 3.4)

### O risco financeiro oculto

O briefing acerta ao chamar egress de "maior risco financeiro oculto". A conta é simples e brutal:
um curso com 20 aulas de 500 MB, assistido por 10.000 assinantes, são **100 TB de egress**. A
US$ 0,085/GB de um CDN tradicional, isso é **~US$ 8.500 em um único curso**. Se a assinatura custa
R$ 29,90/mês, o conteúdo consome a receita antes de qualquer outro custo.

Egress é, portanto, uma **decisão de arquitetura de produto**, não um detalhe de infraestrutura.

### Decisão

| Classe de conteúdo | Onde | Entrega |
|---|---|---|
| Vídeo (aulas) | Provedor de vídeo (Bunny Stream ou Mux) | HLS com playback URL assinada e TTL curto |
| eBooks (PDF/EPUB) | **Cloudflare R2** | URL assinada, TTL ≤ 300 s |
| Packs pesados (PSD, AI, projetos) | **Cloudflare R2** | URL assinada + `download_grant` com contador |
| Avatares, logos, thumbnails | Supabase Storage | Cache público, sem dado sensível |
| Fotos de crianças | **R2, bucket privado dedicado** | URL assinada, TTL ≤ 60 s, jamais cacheada em CDN |

**Por que R2:** egress zero para a internet. É o único item deste projeto onde a escolha de
fornecedor muda a viabilidade do modelo de negócio, não apenas o custo operacional. S3 +
CloudFront resolve tecnicamente e custa uma ordem de grandeza mais.

**Por que não Supabase Storage para mídia pesada:** cobra egress, não empacota HLS, e não oferece
tokenização de playback. Adequado para assets leves; inadequado para o catálogo.

### Fluxo de autorização de download

Nenhuma URL é emitida sem passar por aqui:

```
Cliente pede acesso ao item X
        ↓
API valida JWT (identidade)
        ↓
API resolve entitlements do usuário para o item X   ← autorização de verdade
        ↓  (nenhum entitlement válido → 403, e evento de segurança registrado)
API checa rate limit de download (por usuário e por item)
        ↓
API registra download_grant (quem, o quê, quando, de onde)
        ↓
API assina URL com TTL curto
        ↓
Cliente baixa direto do R2/CDN — sem passar pela API
```

O ponto crítico: **a API nunca faz proxy do arquivo**. Ela autoriza e delega. Isso mantém o custo
de banda da aplicação próximo de zero e permite escalar o serving independentemente da API.

### Proteção contra redistribuição — e honestidade sobre o limite

A skill de segurança (§16) é direta: *"Nenhuma dessas técnicas impede completamente captura de
conteúdo por um usuário autorizado."* O objetivo realista é **elevar o custo do compartilhamento
casual**, não tornar a cópia impossível.

Camadas, em ordem de custo-benefício:

1. **URL assinada com TTL curto** — impede o link de circular. Barato, alto impacto. **MVP.**
2. **Watermark visível por sessão** (e-mail do usuário sobreposto no player e estampado no rodapé
   do PDF gerado sob demanda) — o dissuasor mais eficaz por real investido, porque personaliza a
   responsabilidade. **MVP.**
3. **Limite de downloads e de dispositivos** por entitlement, com detecção de anomalia (mesmo
   usuário, 40 downloads, 12 IPs). **Fase 2.**
4. **HLS com token por segmento** — impede o `ffmpeg` ingênuo apontado para a URL. **Fase 2.**
5. **DRM (Widevine/FairPlay)** — caro em licença e em suporte, quebra em dispositivos antigos.
   **Fase 3, e só se um parceiro de conteúdo exigir contratualmente.**

**Risco residual:** um assinante determinado grava a tela e redistribui. Aceito. O controle
proporcional é detecção e resposta (suspensão da conta), não prevenção — que seria *security
theater* nos termos da §44.

---

## ADR-012 a ADR-015 — LGPD e dados sensíveis (Seção 3.5)

O produto coleta as duas categorias de maior risco da LGPD simultaneamente: **dados de crianças**
(Art. 14) e **convicção religiosa** (Art. 5º, II — dado sensível). Essa combinação coloca o
Congrega em um patamar de exigência acima do SaaS B2B típico.

### Papéis e base legal

| Dado | Controlador | Base legal | Observação |
|---|---|---|---|
| Cadastro de membro | **A igreja** | Art. 7º, V — execução de contrato/procedimentos preliminares | Congrega é **operadora** |
| Vínculo com a igreja (= convicção religiosa) | **A igreja** | **Art. 11, I — consentimento específico e destacado** | Ver nota abaixo |
| Dados de criança no check-in | **A igreja** | **Art. 14 — consentimento parental específico** | Prova do consentimento registrada |
| Conta e cobrança Congrega+ | **Congrega** | Art. 7º, V | Congrega é **controladora** |
| Logs e telemetria | **Congrega** | Art. 7º, IX — legítimo interesse | Com avaliação de impacto documentada |

**Nota sobre convicção religiosa:** o simples fato de constar como membro de uma igreja **é** dado
sensível. Diferente do GDPR, que traz carve-out explícito para entidades religiosas
(Art. 9(2)(d)), a LGPD **não tem hipótese equivalente clara** no Art. 11. Por isso a recomendação
conservadora: **consentimento específico e destacado**, coletado pela igreja no cadastro, com
finalidade declarada e registro de versão do texto consentido. Requer parecer jurídico antes do
go-live — isto é arquitetura, não assessoria jurídica (P4).

### Dados de criança — controles adicionais

Tratados como a classe de maior severidade do sistema. Além do consentimento parental (Art. 14,
§ 1º):

- **Criptografia em nível de aplicação** (AES-256-GCM, chave no secret manager, nunca no banco)
  para: alergias, condições de saúde, foto e observações livres. O DBA não deve conseguir ler esses
  campos com um `SELECT`.
- **Identificador público opaco** — ver D1. Etiqueta impressa jamais carrega ID sequencial.
- **Código de retirada de uso único, com TTL**, hasheado no banco, invalidado no primeiro uso.
- **Log de auditoria obrigatório** em toda leitura de ficha infantil: quem, quando, qual criança,
  qual evento, de qual IP.
- **Retenção mínima:** dados de check-in retidos por 90 dias; após isso, apenas o registro
  agregado de presença, sem detalhe pessoal.
- **Alerta ativo** em tentativa de retirada com código inválido — é o evento que mais importa
  detectar em tempo real neste sistema inteiro.

### Retenção

| Classe | Período | Justificativa | Destino |
|---|---|---|---|
| Check-in infantil (detalhe) | 90 dias | Operacional, sem valor após o evento | Exclusão |
| Cadastro de membro ativo | Enquanto vínculo ativo | Execução de contrato | — |
| Cadastro de membro inativo | 24 meses após desligamento | Retorno de membro é comum | Anonimização |
| Lançamento financeiro | **5 anos** | Prazo fiscal/contábil | Pseudonimização, nunca exclusão |
| Logs de segurança | 12 meses | Investigação de incidente | Exclusão |
| Logs de aplicação | 90 dias | Diagnóstico | Exclusão |

Nunca "guardar tudo para sempre" (§10 da skill de segurança).

### Direito ao esquecimento sem destruir a contabilidade

O conflito é real: o membro pede exclusão, mas a igreja precisa manter o registro do dízimo para
prestação de contas e obrigação fiscal. A resposta da skill (§10) é separar os conceitos:

```
Personal identity  ≠  Financial ledger
```

**Implementação:** o `payment`/lançamento **nunca** referencia PII diretamente. Ele referencia
`member_id`. A exclusão executa:

```sql
-- 1. PII do membro é destruída ou substituída
UPDATE members
   SET full_name   = 'Titular removido',
       email       = NULL,
       phone       = NULL,
       birth_date  = NULL,
       photo_key   = NULL,
       document    = NULL,
       anonymized_at = now()
 WHERE id = @memberId;

-- 2. O ledger permanece íntegro, agora apontando para um titular anônimo
--    Nenhum DELETE. Somatórios, relatórios e fechamentos contábeis continuam corretos.
```

O resultado: o relatório financeiro do exercício continua fechando, a auditoria continua possível,
e não existe mais nenhum dado pessoal associado. É a única forma de atender ao Art. 18, VI sem
quebrar a integridade contábil que a igreja tem obrigação legal de manter.

**Exceção documentada:** se houver processo judicial ou obrigação legal em curso envolvendo o
titular, a exclusão é suspensa com registro do fundamento (Art. 16, I).

### Criptografia e auditoria

- **Em trânsito:** TLS 1.2+ em todo percurso, incluindo API → Postgres (`sslmode=require`).
- **Em repouso:** criptografia de disco do Supabase + criptografia de coluna na aplicação para a
  classe de maior sensibilidade (crianças, documentos).
- **Auditoria de acesso:** tabela `audit_log` append-only registrando `who / what / when / where /
  target / result / correlation_id` (§29) para toda leitura de dado sensível e toda ação
  administrativa. Sem `UPDATE` nem `DELETE` concedidos à role da aplicação nessa tabela.

---

## ADR-020 — Monólito modular, e as costuras que permitem extrair depois

**Decisão: monólito modular.** Um processo de API, um processo de workers, um banco.

**Por quê:** com a equipe da P7 e a escala da P5, microsserviços adicionariam transação
distribuída, consistência eventual entre contextos e sobrecarga operacional para resolver um
problema de escala que ainda não existe. A skill `dotnet-expert` é explícita: não tratar
microsserviços como requisito universal; a `security-cloud-expert` (§45) proíbe introduzir
complexidade distribuída sem justificar o problema resolvido.

**Bounded contexts** (as costuras):

| Contexto | Responsabilidade | Extração futura |
|---|---|---|
| `Identity` | Usuários, credenciais, tokens, sessões | Média — é o mais autocontido |
| `Tenancy` | Igrejas, memberships, papéis | Baixa — muito acoplado a tudo |
| `Congregation` | Membros, famílias, células, eventos | Baixa |
| `Childcare` | Check-in infantil, etiquetas, retirada | **Alta** — dado sensível isolável |
| `Giving` | Dízimos, ofertas, categorias, relatórios | Média |
| `Catalog` | Trilhas, aulas, eBooks, packs | **Alta** — leitura pesada, candidato natural |
| `Billing` | Planos, assinaturas, pagamentos, webhooks | **Alta** — o primeiro a sair, se sair |
| `Entitlement` | Resolução de acesso | Baixa — precisa ser rápido e local |
| `Notification` | Fila, dispatch, preferências | **Alta** |

**Regras de costura**, que são o que separa "monólito modular" de "monólito com pastas":

1. Um contexto **nunca** referencia entidade de outro contexto. Comunicação por ID e por evento de
   domínio.
2. Nenhum `JOIN` SQL entre tabelas de contextos diferentes. Se for preciso, é sinal de que a
   fronteira está errada ou de que falta um read model.
3. Cada contexto tem seu próprio `DbContext`, apontando para o mesmo banco mas com `DbSet`s
   disjuntos.
4. Comunicação assíncrona entre contextos passa pelo **Outbox** — o mesmo mecanismo que seria usado
   se fossem serviços separados. É isso que torna a extração futura um trabalho de infraestrutura,
   e não de reescrita.
5. Um teste de arquitetura (NetArchTest ou equivalente) falha o build se um contexto referenciar o
   namespace de outro fora dos pontos permitidos.

**Risco residual:** disciplina de fronteira depende de revisão humana. O teste de arquitetura
converte parte disso em falha de CI, que é onde esse tipo de regra sobrevive.

---

## ADR-021 — Background jobs em múltiplas réplicas

**Decisão:** `BackgroundService` nativo + **advisory lock do PostgreSQL**. Sem Hangfire, sem
Quartz, sem Redis.

**Por quê não Hangfire:** traz dashboard e persistência prontos, mas também seu próprio schema,
seu próprio modelo de serialização de job e uma dependência pesada para o que, no MVP, são três
jobs periódicos. O dashboard é o argumento real a favor — e ele pode ser adicionado depois se a
operação pedir.

**Por que não Quartz.NET:** poderoso em agendamento (cron complexo, calendários, misfire policies),
mas o produto precisa de "roda a cada N minutos". Introduzir Quartz para isso é a complexidade que
a §45 manda evitar.

**O problema real — execução duplicada.** A skill de segurança (§25) é explícita: *"Nunca assuma
1 pod = 1 job."* Com 3 réplicas no Kubernetes, três `PeriodicTimer` disparam simultaneamente e o
motor de retenção envia três alertas ao mesmo usuário.

**Solução em duas camadas, porque uma só não basta:**

1. **Advisory lock** — só uma réplica executa o ciclo. Evita trabalho duplicado e contenção.
2. **Deduplicação no banco** — `UNIQUE (subscription_id, period_end, alert_window)`. Mesmo que o
   lock falhe (partição de rede, expiração inesperada), a segunda inserção viola a constraint e o
   alerta duplicado não é criado.

A camada 2 é a que realmente garante a propriedade. A camada 1 é otimização. **Correção nunca deve
depender de lock distribuído** — locks falham; constraints de banco não. Esse é o ponto em que a
maioria das implementações erra.

**A armadilha do pooler (P3):** `pg_advisory_lock` de sessão é vinculado à *sessão* Postgres. Sob
Supavisor em *transaction pooling*, a sessão retorna ao pool ao fim da transação e o lock pode ser
liberado ou herdado por outro cliente. Duas saídas válidas:

- **(a)** usar `pg_try_advisory_xact_lock` dentro de uma transação que envolve o ciclo — simples,
  mas prende uma transação por todo o processamento;
- **(b)** manter uma **conexão direta dedicada** (porta 5432, sem pooler) exclusivamente para o
  lock, processando o trabalho por conexões normais do pool.

**Escolha: (b)**, implementada em `PostgresAdvisoryLock`. Mantém as transações de trabalho curtas
— importante porque transação longa segura tuplas mortas e atrapalha o autovacuum.

---

## ADR-022 — CQRS onde há assimetria real

**Decisão:** CQRS apenas em `Catalog` e nos relatórios de `Giving`. O restante usa o modelo de
domínio para leitura e escrita.

**Critério:** CQRS paga quando o modelo de leitura e o de escrita **divergem de fato**. No catálogo
isso acontece: escrita é curadoria rara e transacional (publicar um curso), leitura é massiva,
denormalizada, filtrada por entitlement e cacheável. Em `Congregation`, o cadastro de membro é
lido do mesmo jeito que é escrito — CQRS ali seria só duas classes onde cabia uma.

Aplicar CQRS globalmente produziria centenas de handlers triviais, e a skill `dotnet-expert` é
clara: não introduzir padrão sem justificar o benefício. Onde não há assimetria, `MediatR` +
`AsNoTracking()` + projeção direta para DTO resolve com uma fração da cerimônia.

**Risco residual:** dois estilos convivendo no mesmo código pode confundir quem entra no time.
Mitigação: documentar o critério (este ADR) e mantê-lo em revisão de código.
