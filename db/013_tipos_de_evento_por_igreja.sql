-- Tipo de evento deixa de ser enum e passa a ser dado da igreja.
--
-- O enum `event_type SMALLINT CHECK (1..5)` cobria Culto, Reunião, Estudo,
-- Ensaio e Outro. Cinco rótulos escolhidos por quem escreveu o código, não por
-- quem usa: uma igreja com "Escola Bíblica", "Vigília" e "Célula" não tinha
-- onde encaixá-los, e "Outro" apagava a distinção entre os três.
--
-- A troca custa uma junção a mais na listagem da agenda e devolve o vocabulário
-- para o tenant, que é de quem ele é.

CREATE TABLE event_types (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id   UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id   BIGINT       NOT NULL,

    name        VARCHAR(60)  NOT NULL,

    -- Nome do ícone no conjunto Feather, que é o único que o app carrega.
    -- Guardar o nome, e não um número, faz a linha do banco ser legível sem
    -- consultar uma tabela de-para que viveria só no código do cliente.
    --
    -- A lista de valores aceitos é validada na borda HTTP, não aqui: quais
    -- ícones existem depende da fonte que o app embarca, e essa é uma decisão
    -- de apresentação que muda de versão para versão. Um CHECK aqui viraria
    -- uma migration a cada ícone novo.
    icon        VARCHAR(40)  NOT NULL DEFAULT 'calendar',

    -- Desativado some do formulário e continua na agenda histórica.
    --
    -- Mesmo raciocínio de `giving_categories.is_active`: apagar "Culto" faria
    -- duzentos cultos passados perderem a classificação. A exclusão de verdade
    -- continua existindo — a FK `RESTRICT` abaixo a recusa exatamente quando
    -- ela destruiria história, e libera quando o tipo nunca foi usado.
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,

    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_event_types_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_event_types_public_id UNIQUE (public_id)
);

-- Dois tipos com o mesmo nome partiriam o resumo do mês em duas linhas que
-- deveriam ser uma. `lower(name)` porque "Culto" e "culto" são o mesmo tipo
-- para quem lê a tela.
CREATE UNIQUE INDEX uq_event_types_tenant_nome
    ON event_types (tenant_id, lower(name));

CREATE INDEX ix_event_types_tenant_ativo
    ON event_types (tenant_id, is_active);

-- ---------------------------------------------------------------------------
-- Migração dos dados existentes
-- ---------------------------------------------------------------------------

-- Os quatro rótulos nomeados viram linhas de verdade, para cada igreja que já
-- existe. Não é opinião sobre o que uma igreja deve ter: é preservação do que
-- já estava classificado. A partir daqui a igreja renomeia, desativa ou apaga.
--
-- Igreja criada DEPOIS desta migration nasce sem tipo nenhum — e isso é
-- deliberado, porque `events.event_type_id` é nulo: uma agenda sem vocabulário
-- funciona, só não classifica. Quando existir um fluxo de onboarding de igreja
-- (hoje não há: `Tenant.Create` não é chamado por nenhum handler), é lá que a
-- semeadura destes padrões deve entrar, e não em um gatilho escondido aqui.
INSERT INTO event_types (tenant_id, name, icon)
SELECT t.id, padrao.name, padrao.icon
FROM tenants t
CROSS JOIN (VALUES
    ('Culto',   'heart'),
    ('Reunião', 'users'),
    ('Estudo',  'book-open'),
    ('Ensaio',  'music')
) AS padrao(name, icon)
ON CONFLICT DO NOTHING;

ALTER TABLE events ADD COLUMN event_type_id BIGINT;

-- 1..4 viram a linha correspondente; 5 (`Outro`) vira NULL.
--
-- "Outro" nunca foi um tipo — era a ausência de um. Mantê-lo como linha faria
-- toda igreja começar com uma categoria que significa "não classificado",
-- competindo com o próprio NULL que a coluna já expressa.
UPDATE events e
SET event_type_id = et.id
FROM event_types et
WHERE et.tenant_id = e.tenant_id
  AND et.name = CASE e.event_type
        WHEN 1 THEN 'Culto'
        WHEN 2 THEN 'Reunião'
        WHEN 3 THEN 'Estudo'
        WHEN 4 THEN 'Ensaio'
      END;

-- RESTRICT, e não CASCADE nem SET NULL.
--
-- CASCADE apagaria os eventos junto com o tipo — perda de dado por um clique em
-- "excluir categoria". SET NULL desclassificaria a agenda inteira em silêncio.
-- RESTRICT faz o banco recusar, e a API traduz essa recusa em "este tipo está
-- em uso; desative-o". A correção vem da constraint, não de um `if (!existe)`
-- que seria race condition sob concorrência.
ALTER TABLE events
    ADD CONSTRAINT fk_events_event_type FOREIGN KEY (event_type_id)
        REFERENCES event_types (id) ON DELETE RESTRICT;

CREATE INDEX ix_events_tenant_tipo_id ON events (tenant_id, event_type_id);

-- O enum sai inteiro. Deixá-lo como coluna morta garantiria que um dia alguém
-- escreveria nele e os dois campos divergiriam sem ninguém notar.
DROP INDEX IF EXISTS ix_events_tenant_tipo;
ALTER TABLE events DROP CONSTRAINT IF EXISTS ck_events_tipo;
ALTER TABLE events DROP COLUMN event_type;

ALTER TABLE event_types ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_event_types ON event_types
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
