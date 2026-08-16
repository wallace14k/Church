-- =============================================================================
-- Congrega — Onda 2: núcleo do ChMS (membros e famílias)
-- =============================================================================

CREATE TABLE families (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id    UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id    BIGINT       NOT NULL,
    name         VARCHAR(200) NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_families_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_families_public_id UNIQUE (public_id)
);

CREATE INDEX ix_families_tenant ON families (tenant_id, name);

CREATE TABLE members (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id     UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id     BIGINT       NOT NULL,

    -- Vínculo OPCIONAL com uma conta de login. A maioria dos membros de uma
    -- igreja nunca vai abrir o app: são cadastrados pela secretaria e existem
    -- apenas como registro. Exigir user_id transformaria cadastro em convite e
    -- travaria a digitação da lista que a igreja já tem no papel.
    user_id       BIGINT,
    family_id     BIGINT,

    full_name     VARCHAR(200) NOT NULL,
    email         CITEXT,
    phone         VARCHAR(20),
    birth_date    DATE,
    gender        SMALLINT,                  -- 1=Feminino 2=Masculino 3=NaoInformado
    marital_status SMALLINT,                 -- 1=Solteiro 2=Casado 3=Divorciado 4=Viuvo 5=NaoInformado

    -- Endereço em colunas simples, não em tabela separada: uma pessoa tem um
    -- endereço no ChMS, e normalizar isso adicionaria um JOIN a toda listagem
    -- para resolver um problema que a igreja não tem.
    address_street VARCHAR(200),
    address_number VARCHAR(20),
    address_district VARCHAR(100),
    address_city  VARCHAR(100),
    address_state CHAR(2),
    address_zip   VARCHAR(9),

    status        SMALLINT     NOT NULL DEFAULT 1,  -- 1=Ativo 2=Inativo 3=Transferido 4=Falecido
    membership_date DATE,
    baptism_date  DATE,
    notes         TEXT,
    photo_key     VARCHAR(500),

    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    anonymized_at TIMESTAMPTZ,

    CONSTRAINT fk_members_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    -- RESTRICT, não CASCADE: apagar a conta de login não pode apagar o registro
    -- de membro, que os lançamentos financeiros referenciam.
    CONSTRAINT fk_members_user   FOREIGN KEY (user_id)   REFERENCES users (id)    ON DELETE RESTRICT,
    CONSTRAINT fk_members_family FOREIGN KEY (family_id) REFERENCES families (id) ON DELETE SET NULL,
    CONSTRAINT uq_members_public_id UNIQUE (public_id),
    CONSTRAINT ck_members_status CHECK (status BETWEEN 1 AND 4),
    CONSTRAINT ck_members_gender CHECK (gender IS NULL OR gender BETWEEN 1 AND 3),
    CONSTRAINT ck_members_marital CHECK (marital_status IS NULL OR marital_status BETWEEN 1 AND 5)
);

-- Uma conta de login pertence a no máximo um membro POR IGREJA. A mesma pessoa
-- em duas igrejas tem dois registros de membro e uma única conta — coerente com
-- "identidade é global, pertencimento é contextual".
CREATE UNIQUE INDEX uq_members_user_tenant ON members (tenant_id, user_id)
    WHERE user_id IS NOT NULL;

-- Listagem padrão: ativos daquele tenant, em ordem alfabética.
CREATE INDEX ix_members_tenant_name ON members (tenant_id, full_name) WHERE status = 1;
CREATE INDEX ix_members_family ON members (family_id) WHERE family_id IS NOT NULL;

-- Busca por nome sem diferenciar acento nem caixa. `unaccent` é imutável o
-- suficiente para índice quando envolvido numa função própria; aqui usamos
-- trigramas sobre o nome em minúsculas, que resolve "joao" achar "João".
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

CREATE INDEX ix_members_busca ON members USING GIN (lower(full_name) gin_trgm_ops);

-- Aniversariantes do mês: consulta que toda secretaria faz. Sem o índice por
-- expressão, exigiria varredura completa toda vez.
CREATE INDEX ix_members_aniversario ON members (tenant_id, EXTRACT(MONTH FROM birth_date))
    WHERE birth_date IS NOT NULL AND status = 1;

ALTER TABLE members  ENABLE ROW LEVEL SECURITY;
ALTER TABLE families ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_members ON members
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

CREATE POLICY tenant_isolation_families ON families
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
