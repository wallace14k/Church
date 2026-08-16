-- =============================================================================
-- Outbox: dead letter e lease
-- =============================================================================
-- Mensagem que esgotou as tentativas ou falhou de forma permanente sai da fila,
-- mas NAO e apagada: fica registrada com o erro, para investigacao e eventual
-- reprocessamento manual. Apagar destruiria a evidencia do problema junto com o
-- problema.
ALTER TABLE outbox_messages
    ADD COLUMN IF NOT EXISTS dead_lettered_at TIMESTAMPTZ;

-- O indice parcial precisa excluir tambem as mensagens em dead letter, senao
-- elas seriam varridas para sempre a cada ciclo do dispatcher.
DROP INDEX IF EXISTS ix_outbox_pending;
CREATE INDEX ix_outbox_pending ON outbox_messages (next_attempt_at)
    WHERE processed_at IS NULL AND dead_lettered_at IS NULL;

-- Sustenta o alerta operacional de mensagens que desistiram.
CREATE INDEX IF NOT EXISTS ix_outbox_dead_letter ON outbox_messages (dead_lettered_at DESC)
    WHERE dead_lettered_at IS NOT NULL;
