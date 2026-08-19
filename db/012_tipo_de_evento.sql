-- =============================================================================
-- 012 — Tipo de evento
-- =============================================================================
-- A agenda listava eventos sem distinguir culto de ensaio, e a tela de resumo
-- não tinha como agrupar por natureza. Sem esta coluna a única saída seria
-- adivinhar a categoria pelo título — que erra em "Culto de Oração", é as duas
-- coisas, e apresenta palpite como dado.
--
-- SMALLINT com CHECK, e não texto livre nem enum do Postgres:
--   • texto livre viraria "Culto", "culto", "CULTO" e "Culto " na mesma coluna,
--     e o agrupamento do resumo passaria a depender de normalização em código;
--   • `CREATE TYPE ... AS ENUM` exige migration para acrescentar valor e não
--     aceita remoção — para uma lista que ainda vai crescer, o CHECK é mais
--     barato de evoluir.
--
-- DEFAULT 5 (Outro) porque a coluna nasce em tabela com dados: todo evento já
-- cadastrado precisa de um valor, e "Outro" é o único que não afirma algo falso
-- sobre eventos que ninguém classificou.

ALTER TABLE events
    ADD COLUMN IF NOT EXISTS event_type SMALLINT NOT NULL DEFAULT 5;

ALTER TABLE events
    DROP CONSTRAINT IF EXISTS ck_events_tipo;

ALTER TABLE events
    ADD CONSTRAINT ck_events_tipo CHECK (event_type BETWEEN 1 AND 5);

COMMENT ON COLUMN events.event_type IS
    '1=Culto 2=Reuniao 3=Estudo 4=Ensaio 5=Outro. O resumo da agenda agrupa por '
    'esta coluna; sem ela restaria adivinhar pelo titulo, que erra e inventa.';

-- O resumo por tipo é a consulta nova que esta coluna habilita: "quantos de
-- cada tipo neste mês, nesta igreja". Sem o índice ela varre os eventos todos
-- do tenant a cada abertura da agenda.
CREATE INDEX IF NOT EXISTS ix_events_tenant_tipo ON events (tenant_id, event_type);
