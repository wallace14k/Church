-- =============================================================================
-- Busca de membros insensível a acento
-- =============================================================================
-- `unaccent()` não é IMMUTABLE (depende de um dicionário que pode mudar), então
-- o PostgreSQL recusa usá-la diretamente em índice. O contorno padrão é um
-- wrapper que fixa o dicionário e declara imutabilidade — o que é verdade na
-- prática, já que o dicionário não muda em operação normal.
CREATE OR REPLACE FUNCTION congrega_unaccent(texto text)
RETURNS text
LANGUAGE sql
IMMUTABLE PARALLEL SAFE STRICT
AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, texto) $$;

-- Índice de trigramas sobre a MESMA expressão usada na consulta. Sem lower():
-- o ILIKE do PostgreSQL ja e insensivel a caixa, e o trigrama tambem. Se as duas
-- divergirem, o índice existe e nunca é usado — o pior dos mundos, porque custa
-- escrita e não acelera leitura.
DROP INDEX IF EXISTS ix_members_busca;
CREATE INDEX ix_members_busca
    ON members USING GIN (congrega_unaccent(full_name) gin_trgm_ops);

-- Verificação: "JOAO" precisa achar "João".
DO $$
BEGIN
    IF congrega_unaccent(lower('João da Silva')) <> 'joao da silva' THEN
        RAISE EXCEPTION 'congrega_unaccent não normalizou como esperado.';
    END IF;
    RAISE NOTICE 'congrega_unaccent OK: João -> joao';
END $$;
