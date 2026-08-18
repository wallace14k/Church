-- =============================================================================
-- 008 — Catálogo de planos
-- =============================================================================
-- Sem estas linhas a tabela `plans` fica vazia e TODO checkout responde
-- "plano indisponível" — o endpoint sobe, autentica, valida e nunca cobra.
-- É a mesma classe de falha silenciosa do seed de papéis: nada quebra, nada
-- funciona. Por isso é migration, e não script que alguém lembra de rodar.
--
-- `audience` é controle de acesso, não rótulo de catálogo:
--   1 = Tenant  → ChMS B2B, cobrado da igreja, titular é o tenant
--   2 = User    → Congrega+ B2C, cobrado da pessoa, titular é o usuário
-- O checkout do Congrega+ recusa qualquer plano de audiência 1. Sem essa
-- separação, bastaria o código do plano para uma pessoa física abrir cobrança
-- do plano da igreja.
--
-- `billing_period`: 1 = Mensal, 2 = Anual.
--
-- Preços em CENTAVOS, BIGINT. Nunca decimal de ponto flutuante — a regra do
-- CLAUDE.md vale aqui como vale no livro-caixa.

INSERT INTO plans (code, name, audience, billing_period, price_cents, currency, trial_days, grace_days, is_active)
VALUES
    -- ------------------------------------------------------------------ B2B
    ('chms_basic',      'Congrega Church — Essencial', 1, 1,  9900, 'BRL',  14, 7, TRUE),
    ('chms_pro',        'Congrega Church — Completo',  1, 1, 19900, 'BRL',  14, 7, TRUE),

    -- ------------------------------------------------------------------ B2C
    -- O anual sai por 10 meses: o desconto é o que paga a previsibilidade de
    -- caixa, e deixá-lo implícito no valor evita uma coluna de "desconto" que
    -- teria de ser mantida em sincronia com o preço.
    ('premium_monthly', 'Congrega+ Mensal',            2, 1,  2990, 'BRL',   7, 3, TRUE),
    ('premium_annual',  'Congrega+ Anual',             2, 2, 29900, 'BRL',   7, 3, TRUE)

-- Idempotente pelo mesmo motivo do seed de papéis: as bases de desenvolvimento
-- já existem, e reaplicar não pode duplicar nem falhar. O UPDATE alcança
-- renomeação e correção de preço; `code` é a identidade estável.
ON CONFLICT (code) DO UPDATE
    SET name           = EXCLUDED.name,
        audience       = EXCLUDED.audience,
        billing_period = EXCLUDED.billing_period,
        price_cents    = EXCLUDED.price_cents,
        currency       = EXCLUDED.currency,
        trial_days     = EXCLUDED.trial_days,
        grace_days     = EXCLUDED.grace_days,
        is_active      = EXCLUDED.is_active;
