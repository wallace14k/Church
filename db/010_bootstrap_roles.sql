-- ---------------------------------------------------------------------------
-- Define a senha de congrega_app e congrega_worker.
--
-- A migration AppRoles cria as duas roles SEM senha de propósito: uma migration
-- compilada entra no histórico do Git, e gravar segredo ali seria expô-lo para
-- sempre, mesmo depois de rotacionado. Este script fica de fora do histórico de
-- migrations e lê a senha de variável psql — nunca do arquivo.
--
-- Rodar depois de "dotnet ef database update":
--
--   psql "$CONGREGA_DB_URL" \
--     -v app_password="$CONGREGA_APP_DB_PASSWORD" \
--     -v worker_password="$CONGREGA_WORKER_DB_PASSWORD" \
--     -f db/010_bootstrap_roles.sql
--
-- Idempotente: rodar de novo troca a senha para o valor atual das variáveis, o
-- que é também o procedimento de rotação.
-- ---------------------------------------------------------------------------

\if :{?app_password}
\else
    \warn 'app_password não definido — passe com -v app_password=...'
    \q
\endif

\if :{?worker_password}
\else
    \warn 'worker_password não definido — passe com -v worker_password=...'
    \q
\endif

ALTER ROLE congrega_app    WITH PASSWORD :'app_password';
ALTER ROLE congrega_worker WITH PASSWORD :'worker_password';
