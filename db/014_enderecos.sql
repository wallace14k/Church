-- Endereço vira entidade, e o CEP ganha cache próprio.
--
-- ============================================================================
-- Por que são DUAS tabelas, e não uma
-- ============================================================================
--
-- Há dois conceitos diferentes escondidos na palavra "endereço":
--
--   1. O CEP é dado postal UNIVERSAL. 01001-000 é a Praça da Sé para toda
--      igreja do país, hoje e amanhã. Não pertence a tenant nenhum.
--   2. O endereço é a OCUPAÇÃO de alguém: qual CEP, qual número, que andar.
--      Isso pertence à igreja e não pode vazar entre elas.
--
-- Guardar os dois na mesma tabela obrigaria cada igreja a consultar a ViaCEP
-- pela primeira vez que alguém digitasse um CEP que a igreja ao lado já
-- consultou — e a "busca no banco" pedida no requisito quase nunca acertaria.
--
-- ============================================================================
-- Por que `members` perde as colunas de endereço
-- ============================================================================
--
-- O `db/002_members.sql` registrou a decisão contrária, e com um motivo bom na
-- época: "uma pessoa tem um endereço no ChMS, e normalizar isso adicionaria um
-- JOIN a toda listagem para resolver um problema que a igreja não tem".
--
-- **O que invalidou a decisão foi o endereço passar a ser preciso no EVENTO
-- também.** Manter inline agora significaria repetir seis colunas mais as
-- regras de tipo de residência em duas tabelas, e nada garantiria que as duas
-- cópias das regras continuassem iguais. O JOIN que a decisão original evitava
-- continua evitável: a listagem de membros não seleciona endereço, só o
-- detalhe seleciona.

-- ---------------------------------------------------------------------------
-- Cache de CEP — GLOBAL, sem tenant e sem RLS
-- ---------------------------------------------------------------------------
--
-- Segue o mesmo padrão de `roles` e `permissions`: tabela de referência que
-- não pertence a igreja nenhuma.
--
-- **O que entra aqui é só resposta da ViaCEP, nunca texto digitado por
-- usuário.** Um endereço preenchido à mão quando a ViaCEP não conhece o CEP
-- fica na linha de `addresses`, e não aqui — senão o palpite de uma igreja
-- seria servido como dado autoritativo para todas as outras.
--
-- Efeito colateral aceito: a existência de um CEP no cache revela que ALGUÉM
-- o consultou algum dia. Não revela quem, nem de qual igreja, e o dado é
-- público — mas é um canal lateral fraco e está registrado aqui de propósito,
-- em vez de descoberto depois.
CREATE TABLE postal_codes (
    -- Só os oito dígitos, sem hífen. Normalizar na escrita é o que faz
    -- "01001000" e "01001-000" encontrarem a mesma linha; guardar formatado
    -- criaria duas entradas para o mesmo lugar.
    cep         CHAR(8)      PRIMARY KEY,

    -- Os cinco campos que o requisito manda persistir. O restante do payload
    -- da ViaCEP (unidade, uf, regiao, ibge, gia, ddd, siafi) é descartado.
    logradouro  VARCHAR(200) NOT NULL,
    bairro      VARCHAR(100) NOT NULL,
    localidade  VARCHAR(100) NOT NULL,
    estado      VARCHAR(60)  NOT NULL,

    -- Quando a ViaCEP respondeu. Não expira nada hoje; existe para que um dia
    -- se possa reconsultar o que está velho sem adivinhar a idade da linha.
    fetched_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT ck_postal_codes_cep CHECK (cep ~ '^[0-9]{8}$')
);

-- ---------------------------------------------------------------------------
-- Endereço — do tenant, com RLS
-- ---------------------------------------------------------------------------
CREATE TABLE addresses (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id      UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id      BIGINT       NOT NULL,

    -- Cópia, e não FK para `postal_codes`.
    --
    -- Três motivos: (1) o preenchimento manual precisa destes campos de
    -- qualquer forma, quando nem o cache nem a ViaCEP conhecem o CEP; (2) o
    -- endereço é registro histórico — se a ViaCEP corrigir o nome de uma rua,
    -- o cadastro do membro não deve mudar sozinho debaixo de quem o conferiu;
    -- (3) evita FK de tabela com RLS para tabela sem, que complica o raciocínio
    -- de isolamento sem ganho.
    cep            CHAR(8),
    logradouro     VARCHAR(200),
    bairro         VARCHAR(100),
    localidade     VARCHAR(100),
    estado         VARCHAR(60),

    -- 1=Casa 2=Apartamento
    residence_type SMALLINT     NOT NULL DEFAULT 1,

    -- Número da casa ou do apartamento. Texto, não inteiro: "123-A", "S/N" e
    -- "12 fundos" são endereços reais, e um INT recusaria os três.
    numero         VARCHAR(20),

    -- Andar. Só existe em apartamento — ver o CHECK abaixo.
    andar          VARCHAR(10),

    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_addresses_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_addresses_public_id UNIQUE (public_id),

    CONSTRAINT ck_addresses_residence_type CHECK (residence_type BETWEEN 1 AND 2),
    CONSTRAINT ck_addresses_cep CHECK (cep IS NULL OR cep ~ '^[0-9]{8}$'),

    -- O andar pertence ao apartamento e a mais nada.
    --
    -- Sem esta constraint, uma casa com "3º andar" gravado passaria despercebida
    -- e a etiqueta de correspondência sairia errada. A verificação é do banco, e
    -- não um `if` na aplicação, porque `if` não cobre a importação de planilha
    -- nem o script de correção que alguém rodar às pressas.
    --
    -- **A regra é assimétrica de propósito:** casa NUNCA tem andar, apartamento
    -- PODE ter. Exigir o andar de todo apartamento pareceria mais rigoroso, mas
    -- obrigaria quem digita uma lista de papel que só traz "Apto 32" a inventar
    -- um andar para conseguir salvar. O formulário pede o campo; a constraint
    -- proíbe apenas o que é comprovadamente errado.
    CONSTRAINT ck_addresses_andar CHECK (
        residence_type <> 1 OR andar IS NULL
    )
);

CREATE INDEX ix_addresses_tenant_cep ON addresses (tenant_id, cep) WHERE cep IS NOT NULL;

ALTER TABLE addresses ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_addresses ON addresses
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- ---------------------------------------------------------------------------
-- Vínculos
-- ---------------------------------------------------------------------------

ALTER TABLE members ADD COLUMN address_id BIGINT;
ALTER TABLE events  ADD COLUMN address_id BIGINT;

-- SET NULL, e não CASCADE nem RESTRICT.
--
-- CASCADE apagaria o membro junto com o endereço, o que é desproporcional.
-- RESTRICT travaria a exclusão de um endereço obsoleto. SET NULL deixa o
-- membro sem endereço, que é exatamente o que aconteceu.
ALTER TABLE members
    ADD CONSTRAINT fk_members_address FOREIGN KEY (address_id)
        REFERENCES addresses (id) ON DELETE SET NULL;

ALTER TABLE events
    ADD CONSTRAINT fk_events_address FOREIGN KEY (address_id)
        REFERENCES addresses (id) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------
-- Migração dos endereços que já existem
-- ---------------------------------------------------------------------------
--
-- Cria uma linha de `addresses` para cada membro que tinha QUALQUER campo de
-- endereço preenchido, e liga. Membro sem endereço nenhum não ganha linha
-- vazia: um endereço em branco é ruído que a tela teria de aprender a esconder.
--
-- `address_state` era CHAR(2) (a UF) e `estado` guarda o nome por extenso que a
-- ViaCEP devolve. A UF é mantida como está: converter "SP" em "São Paulo" aqui
-- exigiria uma tabela de-para que este script não tem, e inventar a expansão
-- erraria em quem tinha o campo preenchido à mão com outra coisa. Quem reabrir
-- o cadastro e consultar o CEP recebe o nome por extenso.
-- A ligação é feita por uma coluna temporária que carrega o `members.id`, e
-- **não** casando os campos de endereço de volta.
--
-- Dois membros da mesma família moram no mesmo endereço, com logradouro,
-- número e cidade idênticos: um `UPDATE ... FROM` que casasse por conteúdo
-- encontraria duas linhas candidatas para cada membro e o Postgres escolheria
-- uma arbitrariamente — dois membros poderiam terminar apontando para a mesma
-- linha, deixando a outra órfã. A coluna temporária torna a correspondência
-- exata por construção.
ALTER TABLE addresses ADD COLUMN migracao_member_id BIGINT;

INSERT INTO addresses (
    tenant_id, cep, logradouro, bairro, localidade, estado, numero, residence_type, migracao_member_id
)
SELECT
    m.tenant_id,
    NULLIF(regexp_replace(COALESCE(m.address_zip, ''), '[^0-9]', '', 'g'), '')::CHAR(8),
    m.address_street,
    m.address_district,
    m.address_city,
    m.address_state,
    m.address_number,
    1,                                  -- Casa: o cadastro antigo não distinguia
    m.id
FROM members m
WHERE m.address_street   IS NOT NULL
   OR m.address_number   IS NOT NULL
   OR m.address_district IS NOT NULL
   OR m.address_city     IS NOT NULL
   OR m.address_state    IS NOT NULL
   OR m.address_zip      IS NOT NULL;

UPDATE members m
SET address_id = a.id
FROM addresses a
WHERE a.migracao_member_id = m.id;

ALTER TABLE addresses DROP COLUMN migracao_member_id;

-- Um CEP com menos de 8 dígitos no cadastro antigo não passaria no CHECK. O
-- INSERT acima já o normaliza; esta verificação existe para o script falhar
-- alto se algum ficou torto, em vez de gravar lixo silenciosamente.
DO $$
DECLARE tortos INT;
BEGIN
    SELECT count(*) INTO tortos FROM addresses WHERE cep IS NOT NULL AND cep !~ '^[0-9]{8}$';
    IF tortos > 0 THEN
        RAISE EXCEPTION 'Migração de endereço produziu % CEP(s) inválido(s).', tortos;
    END IF;
END $$;

-- As colunas antigas saem. Deixá-las garantiria que um dia alguém escreveria
-- nelas e as duas cópias divergiriam sem ninguém notar — foi o mesmo raciocínio
-- que tirou `events.event_type`.
ALTER TABLE members DROP COLUMN address_street;
ALTER TABLE members DROP COLUMN address_number;
ALTER TABLE members DROP COLUMN address_district;
ALTER TABLE members DROP COLUMN address_city;
ALTER TABLE members DROP COLUMN address_state;
ALTER TABLE members DROP COLUMN address_zip;
