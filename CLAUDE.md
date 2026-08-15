# Congrega — contexto para o Claude Code

Plataforma SaaS que unifica **dois produtos com receitas distintas**:

- **Congrega Church** — ChMS multi-tenant, B2B, cobrado da igreja
- **Congrega+** — assinatura B2C, cobrada do indivíduo, independente de a igreja ser cliente

## A regra que organiza tudo

> **Identidade é global. Pertencimento é contextual. Direito de acesso é resolvido à parte.**

- `users` **não tem `tenant_id`** — a mesma pessoa pode estar em duas igrejas, ou em nenhuma
- `memberships` liga usuário↔tenant; papéis existem **dentro** de uma membership, nunca soltos
- `entitlements` é o **único** caminho de autorização de conteúdo, seja a origem assinatura,
  compra avulsa, cortesia ou IAP

Quebrar qualquer um dos três invalida o requisito central do produto.

## Antes de mudar qualquer coisa

Leia, nesta ordem: `docs/00-premissas.md` → `docs/01-adrs.md`. Os ADRs registram alternativas
descartadas e o trade-off aceito; contrariar um sem revisá-lo gera retrabalho estrutural.

**Se uma decisão de ADR não sobreviver à implementação, atualize o ADR no mesmo commit.** Já
aconteceu: o ADR-006 previa `SET LOCAL` para o RLS, e a implementação mostrou que `SET LOCAL` só
vale dentro de transação — leituras em autocommit ficariam sem contexto e o RLS negaria tudo. A
revisão está registrada no ADR, com a condição que a invalida.

## Convenções inegociáveis

| Regra | Motivo |
|---|---|
| PK `BIGINT GENERATED ALWAYS AS IDENTITY` | Restrição do briefing |
| `public_id UUID` em tudo que aparece em URL, payload ou etiqueta | PK sequencial exposta é enumeração e IDOR — ver discordância D1 |
| `TIMESTAMPTZ` sempre; UTC na persistência | Regra de negócio converte para `America/Sao_Paulo` na borda |
| Dinheiro em `BIGINT` de centavos | Nunca `float`/`double` |
| `Congrega.Domain` sem **nenhuma** `PackageReference` | Se precisar de uma, a regra não pertence ao domínio |
| Entidade de domínio nunca vira DTO de API | Campo novo vazaria para o contrato público sem ninguém decidir |
| Correção vem de constraint, não de `if (!exists)` | Verificação prévia é race condition sob concorrência |
| FK para dado financeiro é `RESTRICT` | Exclusão de titular **anonimiza**, nunca apaga o ledger |
| Conversão de número para texto usa `InvariantCulture` | Já causou bug real: `1337` viraria `"1.337"` e quebraria o `::bigint` das policies de RLS |

## Segurança — erros que já foram evitados de propósito

Não "simplifique" nenhum destes; cada um tem teste que falha se for removido:

1. **`VerifyOtp` persiste mesmo quando falha.** Sem isso o contador de tentativas volta a zero e
   o limite de 5 nunca é atingido.
2. **Caminho "usuário inexistente" calcula um hash descartado.** Sem isso o erro retorna mais
   rápido e a latência vira oráculo de enumeração.
3. **Contador de tentativas incrementa antes da comparação.**
4. **Reuso de refresh token revoga a family inteira** — não dá para distinguir atacante de
   cliente com retry malfeito.
5. **`subscription_tier` do JWT é dica de interface, nunca autorização.** Acesso a conteúdo
   consulta `entitlements`, sempre.
6. **Advisory lock usa conexão direta (5432).** Lock de sessão não sobrevive ao pooler em
   transaction mode.
7. **Deduplicação de alerta é `UNIQUE (dedupe_key)`**, não o lock distribuído. Locks falham;
   constraints não.
8. **Rate limit de OTP por e-mail é contado no banco**, não em `IMemoryCache` — com 3 réplicas,
   um contador em memória transformaria o limite de 5 em 15.

## Comandos

```bash
dotnet restore
dotnet build      # 0 warnings, 0 erros
dotnet test       # 105 testes, todos passando
```

`TreatWarningsAsErrors` está ligado. As exceções de analisador vivem no `.editorconfig`,
declaradas **uma a uma e com justificativa** — não desligue regra em massa, isso anula o
propósito.

## Estado

- **Verificado:** compila limpo e 105 testes passam (.NET 10.0.103)
- **Entregue:** documentação arquitetural completa, DDL, Onda 1 (identidade, tenancy, RLS,
  autenticação OTP, JWT, refresh com rotação e detecção de reuso), worker de retenção
- **Falta para o login funcionar ponta a ponta:**
  1. migrations do EF Core (não geradas — `db/schema.sql` é a fonte, mas não há timeline)
  2. **dispatcher do Outbox** (as mensagens são gravadas e ninguém as lê — o OTP não chega
     ao usuário; há código iniciado para isso na branch anterior, não integrado aqui)
  3. seed de `roles` e `permissions` (sem as linhas, toda policy reprova)
- **Não iniciado:** núcleo do ChMS, pagamentos, check-in infantil, monorepo React Native (0 linhas)

Sequenciamento recomendado em `docs/06-riscos-e-ondas.md`.
