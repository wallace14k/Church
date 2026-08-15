# Congrega

Plataforma SaaS que unifica dois produtos com modelos de receita distintos:

| Produto | Cliente | Modelo |
|---|---|---|
| **Congrega Church** | A igreja (organização) | ChMS multi-tenant, B2B, cobrado por tenant |
| **Congrega+** | O indivíduo | Assinatura B2C, independente de a igreja ser cliente |

A dualidade é requisito de primeira classe da arquitetura, não caso de borda: **identidade é
global, pertencimento é contextual, direito de acesso é resolvido separadamente**. Um mesmo
usuário pode ser membro de uma igreja cliente, assinante premium individual, ambos ou nenhum.

---

## Documentação

Ler nesta ordem:

| Doc | Conteúdo |
|---|---|
| [00 — Premissas](docs/00-premissas.md) | Premissas declaradas, **4 discordâncias fundamentadas** do briefing e o que não foi verificado |
| [01 — ADRs](docs/01-adrs.md) | 22 decisões arquiteturais com alternativas descartadas, trade-off e risco residual |
| [02 — Autenticação](docs/02-autenticacao.md) | OTP passwordless, JWT, refresh com rotação e detecção de reuso, RBAC + policy |
| [03 — Arquitetura](docs/03-arquitetura.md) | C4 (contexto e container), bounded contexts, caminho de uma requisição, checkout e webhook |
| [04 — Modelagem de dados](docs/04-modelagem-dados.md) | ER, e por que `entitlements` é o único caminho de autorização de conteúdo |
| [05 — Escopo](docs/05-escopo.md) | Classificação MVP / Fase 2 / Fase 3 e a recomendação de corte |
| [06 — Riscos e ondas](docs/06-riscos-e-ondas.md) | Riscos por probabilidade × impacto e o sequenciamento em ondas |
| [`db/schema.sql`](db/schema.sql) | DDL PostgreSQL comentado, com justificativa de cada índice |

---

## Stack

| Camada | Tecnologia |
|---|---|
| Frontend | React Native + Expo (iOS, Android, Web) |
| Backend | .NET (LTS) + C#, Clean Architecture |
| Persistência | Supabase (PostgreSQL) via EF Core |
| Chaves primárias | `BIGINT GENERATED ALWAYS AS IDENTITY` |
| Pagamentos | Abacate.pay (web/B2B) + IAP e Play Billing (B2C in-app) |
| Mídia | Cloudflare R2 + provedor de vídeo com HLS assinado |

---

## Estrutura

```
Congrega.sln
├── src/
│   ├── Congrega.Domain/          # sem NENHUMA dependência externa
│   ├── Congrega.Application/     # casos de uso e portas
│   ├── Congrega.Infrastructure/  # Npgsql, lock, dispatcher
│   └── Congrega.Workers/         # BackgroundServices
├── tests/
│   ├── Congrega.Domain.UnitTests/
│   └── Congrega.Application.UnitTests/
├── db/schema.sql
├── deploy/k8s/
└── docs/
```

O `Congrega.Domain.csproj` não tem uma única `PackageReference`. Essa ausência é a definição
operacional de Clean Architecture neste repositório: se um dia for preciso adicionar uma
dependência ali, a regra sendo escrita provavelmente não pertence ao domínio.

---

## O motor de retenção

Implementação de referência em `src/Congrega.Application/Retention/` e
`src/Congrega.Workers/`. Varre assinaturas próximas do vencimento e dispara alertas
escalonados (D-15, D-7, D-3, D-1 e D+3 em grace period) por e-mail, push e banner.

Quatro propriedades que o desenho garante:

1. **Janelas por faixa, não por igualdade exata.** Se o worker ficar fora do ar no dia exato de
   D-7, o alerta ainda sai quando ele voltar. `daysRemaining == 7` perderia o alerta para sempre.
2. **Deduplicação é do banco.** `UNIQUE (dedupe_key)` em `notification_queue`. O lock
   distribuído evita trabalho duplicado; ele **não** é a garantia de correção — locks falham,
   constraints não.
3. **Conexão direta para o advisory lock.** `pg_advisory_lock` de sessão não sobrevive ao
   pooler do Supabase em *transaction mode*. O lock usa conexão dedicada na porta 5432.
4. **Set-based, sem N+1.** Keyset pagination na leitura; um `INSERT` por lote via `unnest` com
   `ON CONFLICT DO NOTHING` na escrita.

---

## Estado desta entrega

Esta é uma **entrega de arquitetura com implementação de referência**, não um sistema
executável. O que existe:

- ✅ Documentação arquitetural completa (Seções 3 a 6 do briefing)
- ✅ DDL PostgreSQL completo e comentado
- ✅ Motor de retenção implementado em C# com 20 testes unitários
- ✅ Dockerfile, manifests Kubernetes e NetworkPolicy
- ❌ **Código não compilado** — sem SDK .NET no ambiente e política de rede bloqueando o download
- ❌ **Testes não executados** — mesma razão
- ❌ Versões de pacote não verificadas contra o NuGet
- ❌ Módulos de membros, financeiro, check-in e catálogo — não implementados (ver doc 05)

As três primeiras lacunas estão registradas em [`docs/00-premissas.md` §4](docs/00-premissas.md).
Rode `dotnet restore && dotnet build && dotnet test` antes de confiar em qualquer linha do C#.

---

## Skills aplicadas

O repositório carrega as skills usadas na produção desta entrega, em `.agents/skills/`:

`dotnet-expert` · `react-native-expert` · `react-native-best-practices` · `frontend-design` ·
`security-cloud-expert`
