-- =============================================================================
-- Congrega — Onda 2: financeiro (lançamentos e categorias)
-- =============================================================================
--
-- Regime de CAIXA, não de competência: o lançamento registra dinheiro que já
-- entrou ou já saiu, na data em que isso aconteceu. "Contas a pagar" e
-- "orçado × realizado" são outra coisa, e estão na Fase 2 do doc 05 — modelar
-- as duas na mesma tabela agora obrigaria a distinguir data de vencimento de
-- data de pagamento em toda consulta, para um recurso que ninguém pediu ainda.

CREATE TABLE giving_categories (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id   UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id   BIGINT       NOT NULL,

    name        VARCHAR(100) NOT NULL,

    -- 1=Entrada 2=Saida. É a categoria que carrega o sinal, nunca o valor —
    -- ver o CHECK de amount_cents abaixo.
    kind        SMALLINT     NOT NULL,

    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,

    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_giving_categories_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_giving_categories_public_id UNIQUE (public_id),
    CONSTRAINT ck_giving_categories_kind CHECK (kind BETWEEN 1 AND 2)
);

-- Duas categorias com o mesmo nome na mesma igreja quebram o relatório de
-- fechamento em duas linhas que deveriam ser uma. A constraint resolve na
-- escrita; verificar antes de inserir seria race condition sob concorrência.
CREATE UNIQUE INDEX uq_giving_categories_tenant_nome
    ON giving_categories (tenant_id, lower(name));

CREATE TABLE giving_entries (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id     UUID        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id     BIGINT      NOT NULL,

    category_id   BIGINT      NOT NULL,

    -- Opcional: oferta de gazofilácio não tem doador identificado, e exigir
    -- membro impediria de lançar exatamente a receita mais comum da igreja.
    member_id     BIGINT,

    -- Centavos, sempre positivo. O sinal vem de giving_categories.kind.
    -- Permitir valor negativo criaria duas formas de representar uma saída, e
    -- um dia as duas apareceriam somadas no mesmo relatório.
    amount_cents  BIGINT      NOT NULL,

    occurred_on   DATE        NOT NULL,

    -- 1=Dinheiro 2=Pix 3=Cartao 4=Transferencia 5=Cheque 6=Outro
    method        SMALLINT    NOT NULL,

    notes         TEXT,

    -- Quem digitou. Prestação de contas exige saber a origem do registro, e
    -- RESTRICT porque apagar a conta jamais pode apagar a autoria do
    -- lançamento — mesma regra que o ADR-015 aplica a payments.
    recorded_by_user_id BIGINT,

    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_giving_entries_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- RESTRICT: apagar categoria em uso precisa falhar alto. O relatório
    -- histórico depende dela, e um ON DELETE SET NULL deixaria lançamentos
    -- órfãos que não somam em lugar nenhum.
    CONSTRAINT fk_giving_entries_category FOREIGN KEY (category_id)
        REFERENCES giving_categories (id) ON DELETE RESTRICT,

    -- RESTRICT, por ADR-015: exclusão de titular ANONIMIZA o membro, nunca
    -- apaga o histórico financeiro que o referencia.
    CONSTRAINT fk_giving_entries_member FOREIGN KEY (member_id)
        REFERENCES members (id) ON DELETE RESTRICT,

    CONSTRAINT fk_giving_entries_user FOREIGN KEY (recorded_by_user_id)
        REFERENCES users (id) ON DELETE RESTRICT,

    CONSTRAINT uq_giving_entries_public_id UNIQUE (public_id),
    CONSTRAINT ck_giving_entries_amount CHECK (amount_cents > 0),
    CONSTRAINT ck_giving_entries_method CHECK (method BETWEEN 1 AND 6)
);

-- Listagem e fechamento são sempre por período dentro de um tenant.
CREATE INDEX ix_giving_entries_tenant_data
    ON giving_entries (tenant_id, occurred_on DESC);

CREATE INDEX ix_giving_entries_category ON giving_entries (category_id);

-- "Quanto o irmão X contribuiu no ano" — consulta de declaração anual.
CREATE INDEX ix_giving_entries_member ON giving_entries (member_id)
    WHERE member_id IS NOT NULL;

ALTER TABLE giving_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE giving_entries    ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_giving_categories ON giving_categories
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

CREATE POLICY tenant_isolation_giving_entries ON giving_entries
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
