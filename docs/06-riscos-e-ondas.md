# Congrega — Riscos Priorizados e Sequenciamento

> Fecha a entrega. Riscos por probabilidade × impacto e a recomendação de
> implementação em ondas.

---

## 1. Matriz de riscos

`Risco = Probabilidade × Impacto`, conforme a §47 da skill de segurança. Impacto avaliado
sobre dados, dinheiro, privacidade, disponibilidade e reputação.

| # | Risco | Prob. | Impacto | Nível | Mitigação | Risco residual |
|---|---|---|---|---|---|---|
| R1 | **Escopo: dois produtos em paralelo com equipe pequena** | Alta | Alto | 🔴 **Crítico** | Corte de escopo do doc 05: ChMS no MVP, Congrega+ na Fase 2, fundação de billing desde já | Médio — depende de disciplina, não de tecnologia |
| R2 | **Custo de egress destrói a margem do Congrega+** | Média | Crítico | 🔴 **Crítico** | R2 com egress zero; alerta em 120% do baseline; limite de download por entitlement | Baixo, **se** a decisão de fornecedor for tomada antes do primeiro vídeo subir |
| R3 | **Incidente com dado de criança** | Baixa | Crítico | 🔴 **Crítico** | Cripto em coluna, `public_id` opaco, auditoria de leitura, código de retirada de uso único, piloto fechado | Baixo, mas **nunca zero** — é o risco que justifica o plano de resposta a incidente |
| R4 | **Rejeição ou remoção nas lojas por conta do Abacate.pay** | Média | Crítico | 🔴 **Crítico** | Segmentação B2B/B2C do ADR-009; apps separados; IAP no iOS para B2C | **Alto** — regra de loja muda e é interpretada caso a caso |
| R5 | **Vazamento cross-tenant** | Média | Crítico | 🔴 **Crítico** | Global Query Filters **+** RLS como rede; teste de integração que prova o isolamento com o filtro desligado | Baixo |
| R6 | **Premissa do Abacate.pay errada (sem HMAC no webhook)** | Alta | Médio | 🟡 Alto | `IPaymentGateway` isolando o domínio; `fetch-on-notify` como fonte de verdade | Baixo — o desenho já não confia no webhook |
| R7 | **Entrega de OTP falha e trava todos os logins** | Média | Alto | 🟡 Alto | Webhook de bounce, monitoramento de taxa de entrega, provedor secundário na Fase 2 | Médio — é ponto único de falha da autenticação no MVP |
| R8 | **RN Web inadequado para o backoffice financeiro** | Alta | Médio | 🟡 Alto | Discordância D2: avaliar app React DOM separado compartilhando `core` e `api-client` | Médio — se a restrição for mantida, custa produtividade da persona que renova o contrato |
| R9 | **Advisory lock não funciona sob o pooler** | Média | Médio | 🟡 Médio | Conexão direta dedicada; correção garantida por `UNIQUE (dedupe_key)`, não pelo lock | Baixo |
| R10 | **Check-in offline gera duplicidade na sincronização** | Média | Médio | 🟡 Médio | Idempotency key gerada no dispositivo e estável entre tentativas | Médio — exige teste de campo, não de laboratório |
| R11 | **Pico de domingo derruba o check-in** | Média | Alto | 🟡 Alto | Offline-first (o app funciona sem rede), autoscaling agendado | Baixo |
| R12 | **Enumeração por ID sequencial (IDOR)** | Média | Alto | 🟡 Alto | `public_id` UUID nas superfícies expostas (D1); `404` em vez de `403` | Baixo — **se** D1 for aceito; **Alto** se a regra de chave numérica for mantida sem exceção |
| R13 | **Roubo de refresh token** | Baixa | Alto | 🟢 Médio | Keychain/Keystore, cookie `HttpOnly`, rotação com detecção de reuso por family | Baixo |
| R14 | **Redistribuição de conteúdo premium** | Alta | Baixo | 🟢 Médio | URL assinada com TTL curto, watermark por sessão, limite de dispositivos | **Aceito** — nenhuma técnica impede captura por usuário autorizado |
| R15 | **Enquadramento LGPD incorreto (controlador × operador)** | Média | Alto | 🟡 Alto | Parecer jurídico antes do go-live; contrato de operador com cada tenant | Médio — fora do alcance da arquitetura |

### Os quatro que decidem o projeto

Se apenas quatro puderem ser atacados antes do primeiro cliente, são **R1, R2, R4 e R5**.

- **R1 e R4** são resolvidos por **decisão de negócio**, e agora — adiar custa retrabalho
  estrutural, não apenas tempo.
- **R2** é resolvido por **escolha de fornecedor**, antes do primeiro vídeo subir. Depois de
  100 TB no lugar errado, migrar custa exatamente o egress que se queria evitar.
- **R5** é resolvido por **teste automatizado**, não por revisão de código. Isolamento que
  depende de alguém lembrar não é isolamento.

---

## 2. Sequenciamento em ondas

Ondas, não sprints: cada uma termina com algo verificável em produção.

### Onda 0 — Fundação (semanas 1–3)

Nada de funcionalidade. É o andaime que todo o resto assume existir.

- Monorepo, solution .NET, CI com lint, typecheck, testes e SAST
- Migrations do EF Core + `schema.sql` inicial aplicado
- Docker multi-stage non-root, manifests k8s, secrets, probes
- Serilog + OpenTelemetry + correlation ID atravessando RN → API → Postgres
- Testcontainers com Postgres real no pipeline
- **Portão:** um endpoint `/health` em produção, com trace ponta a ponta visível

> Pular a Onda 0 para "começar a entregar valor" é a decisão que mais atrasa projetos deste
> tipo. Observabilidade adicionada depois nunca alcança o mesmo nível.

### Onda 1 — Identidade e tenancy (semanas 4–7)

- OTP passwordless completo, com rate limiting e proteção contra enumeration
- JWT com as claims definidas; refresh com rotação e detecção de reuso
- `memberships`, papéis, permissões, policies
- Global Query Filters + RLS + **o teste que prova o isolamento com o filtro desligado**
- Troca de tenant e o caso do assinante sem igreja
- **Portão:** teste de integração de isolamento cross-tenant passando no CI (**mitiga R5**)

### Onda 2 — Núcleo do ChMS (semanas 8–13)

- Membros e famílias
- Financeiro: lançamentos, categorias, relatório de fechamento
- Calendário de eventos
- App RN: navegação, design system, TanStack Query, storage seguro de token
- **Portão:** uma igreja piloto operando o cadastro e o financeiro de verdade

### Onda 3 — Monetização (semanas 14–18)

- `IPaymentGateway` + adaptador Abacate.pay
- Checkout com `Idempotency-Key`; webhook com HMAC, replay protection e `fetch-on-notify`
- Máquina de estados da assinatura; `entitlements`
- Outbox + dispatcher de notificações
- **Motor de retenção** (entregável 6.5)
- **Portão:** cobrança real recebida, webhook duplicado comprovadamente inócuo (**mitiga R6**)

### Onda 4 — Check-in infantil (semanas 19–23)

Deliberadamente por último dentro do MVP: depende de identidade, autorização, auditoria e
fila offline, e é o item que menos tolera improviso.

- Fila offline SQLite com idempotency key
- Etiqueta com `public_id`, código de retirada hasheado de uso único
- Criptografia em coluna, auditoria de leitura, consentimento parental
- **Portão:** piloto com 3–5 igrejas + parecer jurídico (**mitiga R3**)

### Onda 5 — Congrega+ (Fase 2, a partir da semana 24)

- Catálogo, R2, URLs assinadas, watermark
- Paywall por plataforma, IAP e Play Billing com validação server-side de recibo
- eBooks primeiro; vídeo depois
- **Portão:** aprovação na App Store com o fluxo de IAP (**mitiga R4**)

---

## 3. Decisões que precisam ser tomadas antes da Onda 1

Não são tarefas de engenharia. São decisões que, adiadas, geram retrabalho estrutural:

| Decisão | Quem decide | Por que agora |
|---|---|---|
| **Aceitar `public_id` ao lado da PK numérica (D1)** | Arquitetura + produto | Adicionar a coluna depois exige migração de dados e mudança em todo contrato de API |
| **App React DOM separado para o backoffice (D2)** | Produto | Define a estrutura do monorepo desde o primeiro commit |
| **Fornecedor de mídia (R2)** | Negócio | Migrar 100 TB custa exatamente o egress que se queria evitar |
| **Sequenciamento ChMS antes de Congrega+ (R1)** | Negócio | Define para onde vão as próximas 20 semanas |
| **Parecer jurídico sobre Art. 11 e Art. 14** | Jurídico | Pode alterar o modelo de consentimento e, com ele, o schema |

---

## 4. Como saber se está dando certo

Métricas que valem mais que velocity:

| Sinal | Alvo | O que revela |
|---|---|---|
| Tempo de commit até produção | < 30 min | Saúde do pipeline |
| Cobertura de teste no domínio | > 80% | A camada onde o teste é barato e vale mais |
| Webhooks não processados | ~ 0 | Receita presa em fila |
| Taxa de entrega de OTP | > 99% | Autenticação funcionando (**R7**) |
| Egress mensal vs. receita | < 15% | Viabilidade do Congrega+ (**R2**) |
| Churn mensal do ChMS | < 3% | Se o produto vale o que cobra |
| Alertas de retenção → renovações | medido | Se o motor do 6.5 se paga |
