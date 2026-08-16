-- ---------------------------------------------------------------------------
-- Adota um banco já existente na timeline de migrations.
--
-- Bases criadas antes das migrations — as de desenvolvimento, montadas pelo
-- docker-entrypoint-initdb.d — já têm o schema e o seed. Rodar `dotnet ef
-- database update` nelas tentaria criar tudo de novo e falharia no primeiro
-- CREATE TABLE.
--
-- Este script registra as duas primeiras migrations como aplicadas SEM
-- executá-las. A partir daí o banco existente e um banco novo convergem: ambos
-- recebem só as migrations seguintes.
--
-- NÃO rode isto em banco vazio. Marcaria como criado um schema que não existe,
-- e o erro só apareceria na primeira consulta.
--
--   psql "$CONGREGA_DB" -f db/005_baseline_aplicado.sql
-- ---------------------------------------------------------------------------

BEGIN;

-- O EF Core cria esta tabela sozinho na primeira aplicação; aqui ela precisa
-- existir antes, já que estamos escrevendo nela sem passar pela ferramenta.
-- O nome e os tipos têm de bater com o que o provider espera, senão a próxima
-- migration falha ao ler o histórico.
CREATE TABLE IF NOT EXISTS __congrega_migrations (
    "MigrationId"    VARCHAR(150) NOT NULL,
    "ProductVersion" VARCHAR(32)  NOT NULL,
    CONSTRAINT "PK___congrega_migrations" PRIMARY KEY ("MigrationId")
);

-- Recusa em banco vazio: sem `tenants`, não há schema para adotar, e marcar o
-- baseline seria uma mentira que só apareceria depois.
DO $$
BEGIN
    IF to_regclass('public.tenants') IS NULL THEN
        RAISE EXCEPTION
            'Banco vazio: não há schema para adotar. Use "dotnet ef database update".';
    END IF;
END $$;

INSERT INTO __congrega_migrations ("MigrationId", "ProductVersion")
VALUES ('20260816134707_BaselineSchema',          '10.0.11'),
       ('20260816134842_SeedRolesAndPermissions', '10.0.11')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
