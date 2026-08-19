-- =============================================================================
-- 009 — Índice de pagamentos por titular pessoa física
-- =============================================================================
-- `payments` nasceu com três índices (`schema.sql`), e nenhum deles serve à
-- consulta que o histórico de pagamentos do Congrega+ faz:
--
--   ix_pay_sub      (subscription_id, created_at DESC)
--   ix_pay_tenant   (tenant_id, created_at DESC) WHERE tenant_id IS NOT NULL
--   ix_pay_gateway  (gateway_charge_id)          WHERE gateway_charge_id IS NOT NULL
--
-- O B2B tem o dele (`ix_pay_tenant`); o B2C não tinha equivalente. Sem este
-- índice, `WHERE user_id = @eu ORDER BY created_at DESC LIMIT 50` vira Seq Scan
-- na tabela inteira de pagamentos da plataforma — a cada abertura da aba de
-- assinatura, por assinante. Cresce com o número de clientes, não com o
-- histórico de quem está olhando.
--
-- Parcial (`WHERE user_id IS NOT NULL`) e com a mesma forma do `ix_pay_tenant`
-- de propósito: pagamento de igreja tem `user_id` nulo e não pertence a este
-- índice. A ordem `created_at DESC` acompanha a cláusula da consulta para que o
-- `LIMIT` seja resolvido pela varredura do índice, sem sort.
--
-- IF NOT EXISTS porque bancos de desenvolvimento podem já ter recebido o índice
-- manualmente; a migration precisa ser reaplicável sem erro.

CREATE INDEX IF NOT EXISTS ix_pay_user ON payments (user_id, created_at DESC)
    WHERE user_id IS NOT NULL;

COMMENT ON INDEX ix_pay_user IS
    'Histórico de pagamentos do assinante Congrega+. Espelha ix_pay_tenant, que '
    'cobre o mesmo acesso para o titular pessoa jurídica.';
