# Congrega — Premissas e Discordâncias

> Documento de abertura. Tudo o que segue nos demais documentos assume as premissas
> declaradas aqui. Se alguma delas for falsa, o impacto está anotado ao lado.

---

## 1. Identidade do produto

**Congrega** é uma plataforma única com dois produtos comerciais distintos:

| Produto | Sigla interna | Cliente | Modelo |
|---|---|---|---|
| **Congrega Church** | ChMS | A igreja (organização) | SaaS B2B, assinatura por tenant |
| **Congrega+** | Hub | O indivíduo | Assinatura B2C, independente de tenant |

A dualidade é tratada como requisito de primeira classe: **identidade é global, pertencimento é
contextual**. Um `user` existe uma única vez na plataforma; sua relação com igrejas é uma
coleção de `memberships`, e seu acesso a conteúdo é uma coleção de `entitlements`. Nenhum dos
dois é derivado do outro.

---

## 2. Premissas declaradas

Numeradas para referência cruzada nos ADRs. Cada uma traz o impacto caso se revele falsa.

### P1 — Abacate.pay: contrato desconhecido

Não tenho documentação verificada da API do Abacate.pay. Assumo o comportamento típico de
gateways brasileiros de PIX/cartão:

- criação de cobrança via `POST` autenticado por API key, retornando um identificador e uma URL
  de checkout hospedada;
- notificação assíncrona por webhook `POST` com corpo JSON;
- assinatura do webhook por HMAC-SHA256 em header, com timestamp para proteção contra replay;
- suporte a PIX e cartão de crédito recorrente.

**Consequência de projeto:** o domínio **nunca** referencia o Abacate.pay. Toda interação passa
por `IPaymentGateway`, e o adaptador concreto é a única peça que precisa mudar se qualquer
premissa acima estiver errada.

> **Se o Abacate.pay não assinar webhooks por HMAC**, isso vira um risco alto e a mitigação é
> outra: consultar o status da cobrança na API do gateway a cada webhook recebido, tratando o
> webhook apenas como gatilho (`fetch-on-notify`), nunca como fonte de verdade. O desenho já
> comporta essa troca — ver ADR-015.

### P2 — Versão do .NET

Assumo **.NET 10** como o LTS corrente em agosto/2026 (LTS de novembro/2025). **Não consegui
verificar contra a documentação oficial**: a política de rede desta sessão bloqueia
`builds.dotnet.microsoft.com` e o domínio de docs da Microsoft. A skill `dotnet-expert` exige
verificação de versão em fonte oficial antes de afirmar detalhes — como não pude cumprir isso,
registro a premissa em vez de apresentá-la como fato.

**Impacto se falso:** baixo. O código do entregável 6.5 usa apenas APIs estáveis desde o .NET 8
(`PeriodicTimer`, `BackgroundService`, `TimeProvider`, nullable reference types). Ajusta-se o
`<TargetFramework>` e segue.

### P3 — Supabase é PostgreSQL gerenciado

Assumo Supabase **hospedado** (não self-hosted), usado essencialmente como Postgres gerenciado +
Storage. Assumo que o acesso do EF Core se dá por conexão Postgres direta, e que o pooler
(Supavisor) está disponível nos modos *transaction* e *session*.

**Esta premissa tem consequência técnica pesada e frequentemente ignorada:** em *transaction
pooling*, a conexão volta ao pool ao fim de cada transação. Isso significa que:

- `SET LOCAL` funciona (é transacional) → **RLS é viável**;
- `pg_advisory_lock` de sessão **não funciona de forma confiável** → o lock distribuído do worker
  precisa de conexão direta ou de lock transacional. Ver ADR-021.

### P4 — Papéis LGPD

Assumo que, para os dados de membros de uma igreja, **a igreja é a controladora** e a **Congrega
é a operadora** (Art. 5º, VI e VII da LGPD). Para dados de conta, cobrança e do assinante
Congrega+, a **Congrega é controladora**.

**Impacto se falso:** alto. Muda quem responde por requisições de titular, quem assina o contrato
de tratamento e o desenho dos fluxos de exclusão. Requer validação jurídica antes do go-live.

### P5 — Escala e volumetria

Conforme briefing: 300–2.000 tenants, 50.000–300.000 usuários, catálogo de mídia pesada. Assumo
distribuição desigual — a maior igreja terá ~50× a mediana. Assumo pico de escrita concentrado em
domingo de manhã (check-in), com carga base baixa no resto da semana.

**Consequência:** o gargalo de disponibilidade não é throughput médio, é **pico curto e
previsível**. Isso favorece autoscaling agendado e offline-first no check-in em vez de
sobredimensionamento permanente.

### P6 — Fuso, moeda e idioma

`America/Sao_Paulo` para regras de negócio; **`timestamptz` (UTC) na persistência**, sem exceção.
Moeda BRL, armazenada em centavos como `BIGINT` — nunca `float`/`double`. Idioma pt-BR na
interface, inglês nos identificadores de código.

### P7 — Equipe

Assumo time pequeno (3–6 pessoas) na fase MVP, sem SRE dedicado. **Esta é a premissa que mais
influencia as recomendações de arquitetura**: ela é a razão principal para monólito modular em vez
de microsserviços, e para preferir mecanismos nativos do PostgreSQL a introduzir Redis, Kafka ou
service mesh.

### P8 — Provedores de e-mail e push

Não especificados no briefing. Abstraídos por `IEmailSender` e `IPushSender`. Assumo um provedor
transacional com webhook de bounce/complaint (a entrega de OTP por e-mail é caminho crítico de
autenticação — bounce silencioso trava o login do usuário).

---

## 3. Onde discordo do briefing

A Seção 7 do briefing pede discordância explícita. Concordância acrítica não teria valor. São
quatro pontos, em ordem de gravidade.

### D1 — Chave numérica exposta conflita com segurança infantil 🔴

**Restrição do briefing:** chaves primárias numéricas (`INT`/`BIGINT` identity), "regra estrita,
sem exceções".

**Conflito:** a skill `security-cloud-expert`, §11, determina: *"Nunca permita que um simples ID
incremental revele informações sobre crianças."* Um `BIGINT` sequencial exposto em URL entrega de
graça: enumeração de recursos (`/children/1`, `/children/2`, …), inferência de volume de negócio
(quantas crianças/membros/igrejas existem) e ampliação de qualquer falha de autorização de um
registro para a base inteira. É o vetor IDOR/BOLA clássico, nº 1 do OWASP API Security Top 10.

**Recomendação:** manter a restrição **na íntegra para o modelo físico** — todas as PKs e FKs
continuam `BIGINT GENERATED ALWAYS AS IDENTITY`, com todos os ganhos de localidade de índice,
tamanho de página e performance de join que motivaram a regra. **Adicionar** uma coluna
`public_id UUID NOT NULL DEFAULT gen_random_uuid()` com índice único apenas nas tabelas cujos
registros aparecem em URL, payload de API ou etiqueta impressa: `tenants`, `users`, `children`,
`resource_packs`, `subscriptions`, `download_grants`.

A API expõe `public_id`; o banco continua joinando por `BIGINT`. A restrição é respeitada onde ela
gera valor (performance de persistência) e neutralizada onde ela gera risco (superfície pública).

**Se a regra for reafirmada sem exceção**, o desenho ainda funciona, mas registro o risco residual
como **Alto** e ele passa a exigir rate limiting agressivo por objeto e auditoria de acesso
sequencial como controle compensatório — controles mais caros e menos eficazes que uma coluna a
mais.

### D2 — React Native Web é a ferramenta errada para o backoffice financeiro 🟡

**Restrição do briefing:** React Native para iOS, Android e Web a partir de um único código-base.

**Conflito:** funciona muito bem para o app do membro e para o check-in. Funciona mal para a tela
que a secretária usa oito horas por dia: grade densa de lançamentos financeiros, edição em massa,
navegação por teclado, atalhos, seleção múltipla, exportação e **impressão de relatórios**. React
Native Web não tem tabela nativa, tem suporte fraco a `@media print`, e reimplementar grid com
teclado sobre `FlatList` custa mais do que usar a plataforma.

A própria skill `react-native-expert` avisa: *"React Native Web não é simplesmente um celular no
navegador"* e recomenda componentes específicos para Web em vez de esticar interface mobile.

**Recomendação:** manter RN universal para **membro, líder e check-in** (onde o compartilhamento
paga). Para o **backoffice administrativo e financeiro**, avaliar um app React DOM separado dentro
do mesmo monorepo, consumindo os mesmos pacotes `@congrega/core` e `@congrega/api-client`. O
compartilhamento que importa — tipos, contratos de API, regras de domínio — é preservado; só a
camada de apresentação diverge, que é exatamente onde as necessidades divergem de fato.

**Custo de não fazer:** produtividade da persona que mais usa o sistema, e que é quem renova o
contrato.

### D3 — Supabase entrega pouco valor neste desenho 🟡

**Restrição do briefing:** Supabase (PostgreSQL) via EF Core.

**Observação:** decidido que a API .NET é a autoridade única de identidade (ADR-005) e que o EF
Core é dono do schema (ADR-008), o Supabase fica reduzido a **Postgres gerenciado + Storage**.
Auth, PostgREST, Realtime e Edge Functions — que são o valor diferencial do produto — ficam
desligados. Paga-se pela plataforma e usa-se a fatia que qualquer Postgres gerenciado entrega.

**Recomendação:** aceitar como restrição (é decisão de negócio legítima, e o custo inicial do
Supabase é competitivo), mas com dois cuidados operacionais registrados: **(a)** o pooler em modo
transaction quebra locks de sessão — ver P3 e ADR-021; **(b)** Storage do Supabase não é adequado
para o catálogo de vídeo pesado, por custo de egress e ausência de empacotamento HLS — ver
ADR-010. A saída do Supabase, se um dia acontecer, é barata justamente porque nada além de
Postgres está sendo usado.

### D4 — Abacate.pay não é utilizável para conteúdo B2C dentro do app iOS 🔴

**Restrição do briefing:** gateway de pagamento Abacate.pay.

**Conflito:** conteúdo digital consumido dentro do app está sujeito à obrigatoriedade de compra
in-app nas lojas. Não é questão de engenharia — é de conformidade contratual com Apple e Google, e
a sanção é remoção do app.

**Recomendação:** segmentar por natureza da venda, não por preferência técnica. A assinatura do
**ChMS é serviço B2B vendido a uma organização** e fica legitimamente fora do IAP; a assinatura
**Congrega+ vendida a pessoa física** exige IAP no iOS. O domínio absorve as duas origens através
de um único modelo de `Subscription` com `source` discriminador e resolução de acesso via
`entitlements`. Detalhamento completo no ADR-009 — é a decisão de maior impacto financeiro do
projeto.

---

## 4. O que não foi verificado nesta entrega

Registro explícito, para que nada aqui seja lido como validado quando não foi:

| Item | Estado | Motivo |
|---|---|---|
| Compilação do código C# | ❌ Não compilado | Sem SDK .NET no ambiente; política de rede bloqueia o download |
| Execução dos testes unitários | ❌ Não executado | Idem |
| Contrato real do Abacate.pay | ❌ Não verificado | Sem documentação disponível — ver P1 |
| Versão LTS do .NET | ❌ Não verificado | Política de rede bloqueia docs oficiais — ver P2 |
| Regras vigentes de Apple/Google | ⚠️ Não verificado | Mudam com frequência e por jurisdição; ver ADR-009 |
| Enquadramento jurídico LGPD | ⚠️ Requer parecer | Arquitetura ≠ assessoria jurídica; ver ADR-011 |
| Disponibilidade da marca "Congrega" | ❌ Não verificado | Sem consulta a INPI ou registro.br |

O código foi escrito com rigor — usings corretos, nullable habilitado, `async`/`await` sem
`.Result`/`.Wait()` — mas **"escrito com rigor" não é "compilado"**, e a diferença importa.
