-- =============================================================================
-- Congrega — Onda 4: check-in infantil
-- =============================================================================
--
-- A classe de dado de maior severidade do sistema (ADR-014). O doc 05 é
-- explícito: "um incidente aqui não é multa — é o fim da marca". Os controles
-- abaixo não são defensivos por precaução; são os portões que o próprio escopo
-- declara inegociáveis por prazo.
--
-- Quatro decisões estão assadas neste DDL, e nenhuma delas dá para acrescentar
-- depois sem migrar dado:
--
--   1. Campos sensíveis são BYTEA cifrado NA APLICAÇÃO (AES-256-GCM), não TEXT.
--      O critério de aceitação do ADR é literal: o DBA não pode ler alergia com
--      um SELECT. Criptografia de disco não satisfaz isso — protege o disco
--      roubado, não a consulta autenticada. E não é `pgcrypto`: a chave iria
--      como argumento de função, aparecendo no log de query.
--
--   2. `public_id` UUID em criança e check-in. É o que vai impresso na etiqueta
--      e em toda URL. Uma etiqueta com id sequencial deixa qualquer pessoa na
--      fila do berçário inferir quantas crianças há e endereçar as outras.
--
--   3. Código de retirada guardado como HASH, com validade e uso único. Em
--      texto claro, o dump do banco é a lista de senhas de retirada de todas as
--      crianças da plataforma.
--
--   4. `idempotency_key` UNIQUE no check-in. A fila offline (Wi-Fi ruim é
--      requisito real, não desejável) reapresenta a mesma operação; sem a
--      constraint, a criança entra duas vezes e a contagem do berçário mente.

-- -----------------------------------------------------------------------------
-- children
-- -----------------------------------------------------------------------------
CREATE TABLE children (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id      UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id      BIGINT       NOT NULL,

    full_name      VARCHAR(200) NOT NULL,
    birth_date     DATE         NOT NULL,

    -- A criança PODE ter ficha de membro, e normalmente não tem. Manter o
    -- vínculo opcional evita obrigar a secretaria a cadastrar como membro toda
    -- criança que aparece uma vez num culto.
    member_id      BIGINT,

    -- ------------------------------------------------------------------ cifrado
    -- Ver decisão 1 no topo. NULL significa "não informado" e não é segredo —
    -- por isso a coluna é anulável e o NULL não é cifrado.
    allergies_enc     BYTEA,
    health_notes_enc  BYTEA,

    -- REFERÊNCIA à foto, cifrada — não os bytes da imagem. Guardar binário de
    -- foto em coluna não escala, e "Fornecedor de mídia" é decisão pendente
    -- declarada no TODO. Quando houver storage, isto guarda a chave do objeto.
    photo_ref_enc     BYTEA,

    is_active      BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_children_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- RESTRICT e não CASCADE: apagar a ficha de membro não pode levar junto a
    -- ficha da criança e o histórico de quem a retirou.
    CONSTRAINT fk_children_member FOREIGN KEY (member_id)
        REFERENCES members (id) ON DELETE RESTRICT,

    CONSTRAINT uq_children_public_id UNIQUE (public_id),

    -- Nascimento no futuro é erro de digitação de ano, e passaria despercebido
    -- porque a idade calculada sairia negativa em vez de estourar.
    CONSTRAINT ck_children_nascimento CHECK (birth_date <= CURRENT_DATE)
);

COMMENT ON COLUMN children.allergies_enc IS
    'AES-256-GCM cifrado na aplicação, chave no secret manager. ADR-014: o DBA '
    'não deve conseguir ler este campo com um SELECT.';

CREATE INDEX ix_children_tenant_nome ON children (tenant_id, full_name);

ALTER TABLE children ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_children ON children
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- -----------------------------------------------------------------------------
-- child_guardians — quem tem autorização para retirar
-- -----------------------------------------------------------------------------
-- Tabela própria, e não uma coluna `guardian_id` em `children`: uma criança tem
-- mais de um responsável autorizado (pai, mãe, avó), e quem pode BUSCAR não é
-- necessariamente quem tem a guarda. Modelar como coluna forçaria a escolher um
-- e perderia exatamente a informação que o balcão do berçário precisa consultar.
CREATE TABLE child_guardians (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id    BIGINT       NOT NULL,
    child_id     BIGINT       NOT NULL,
    member_id    BIGINT       NOT NULL,

    relationship VARCHAR(50)  NOT NULL,   -- "Mãe", "Pai", "Avó", "Responsável"

    -- A distinção que importa no balcão: nem todo responsável cadastrado está
    -- autorizado a retirar. Um acordo de guarda pode excluir um dos pais, e o
    -- sistema precisa conseguir representar isso.
    can_pickup   BOOLEAN      NOT NULL DEFAULT TRUE,

    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_guardians_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_guardians_child FOREIGN KEY (child_id)
        REFERENCES children (id) ON DELETE CASCADE,
    CONSTRAINT fk_guardians_member FOREIGN KEY (member_id)
        REFERENCES members (id) ON DELETE RESTRICT,

    -- O mesmo responsável não entra duas vezes para a mesma criança.
    CONSTRAINT uq_guardians_child_member UNIQUE (child_id, member_id)
);

CREATE INDEX ix_guardians_child ON child_guardians (child_id);

ALTER TABLE child_guardians ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_guardians ON child_guardians
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- -----------------------------------------------------------------------------
-- child_checkins
-- -----------------------------------------------------------------------------
CREATE TABLE child_checkins (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- O identificador que vai IMPRESSO na etiqueta. Ver decisão 2 no topo.
    public_id      UUID         NOT NULL DEFAULT gen_random_uuid(),

    tenant_id      BIGINT       NOT NULL,
    child_id       BIGINT       NOT NULL,

    -- Âncora no evento concreto — é para isso que o calendário da Onda 2 existe
    -- sem recorrência: o check-in precisa de uma ocorrência real, não de regra.
    event_id       BIGINT       NOT NULL,

    checked_in_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    checked_in_by  BIGINT       NOT NULL,   -- users.id do voluntário no balcão

    -- ------------------------------------------------------- código de retirada
    -- HMAC com pepper, nunca o código. Ver decisão 3 no topo.
    pickup_code_hash       BYTEA       NOT NULL,
    pickup_code_expires_at TIMESTAMPTZ NOT NULL,

    -- Preenchidos na retirada. `picked_up_at` não nulo É o uso único: a segunda
    -- tentativa encontra a linha já consumida.
    picked_up_at           TIMESTAMPTZ,
    picked_up_by_member_id BIGINT,

    -- 1=Presente 2=Retirado 3=Expirado
    status         SMALLINT     NOT NULL DEFAULT 1,

    -- Ver decisão 4 no topo: a chave estável que a fila offline reapresenta.
    idempotency_key VARCHAR(100) NOT NULL,

    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_checkins_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_checkins_child FOREIGN KEY (child_id)
        REFERENCES children (id) ON DELETE RESTRICT,
    CONSTRAINT fk_checkins_event FOREIGN KEY (event_id)
        REFERENCES events (id) ON DELETE RESTRICT,
    CONSTRAINT fk_checkins_operador FOREIGN KEY (checked_in_by)
        REFERENCES users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_checkins_retirado_por FOREIGN KEY (picked_up_by_member_id)
        REFERENCES members (id) ON DELETE RESTRICT,

    CONSTRAINT uq_checkins_public_id UNIQUE (public_id),
    CONSTRAINT uq_checkins_idempotency UNIQUE (idempotency_key),
    CONSTRAINT ck_checkins_status CHECK (status BETWEEN 1 AND 3),

    -- Retirada exige QUEM retirou. Sem o CHECK, um bug deixaria a criança
    -- marcada como retirada sem registro de por quem — e é exatamente essa a
    -- pergunta que se faz quando algo dá errado.
    CONSTRAINT ck_checkins_retirada CHECK (
        (picked_up_at IS NULL AND picked_up_by_member_id IS NULL)
        OR (picked_up_at IS NOT NULL AND picked_up_by_member_id IS NOT NULL)
    )
);

COMMENT ON CONSTRAINT uq_checkins_idempotency ON child_checkins IS
    'A garantia de que a fila offline reapresentando a mesma operação não faz a '
    'criança entrar duas vezes. Vem do banco — verificação prévia em código tem '
    'janela sob concorrência, e no berçário há vários tablets sincronizando juntos.';

-- Uma criança presente por vez, por evento. UNIQUE parcial: só vale enquanto o
-- check-in está aberto, então a mesma criança pode voltar no culto seguinte.
CREATE UNIQUE INDEX uq_checkins_presente ON child_checkins (child_id, event_id)
    WHERE status = 1;

-- A consulta do balcão: quem está presente neste evento agora.
CREATE INDEX ix_checkins_evento_presente ON child_checkins (event_id)
    WHERE status = 1;

ALTER TABLE child_checkins ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_checkins ON child_checkins
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- -----------------------------------------------------------------------------
-- parental_consents — a prova do Art. 14
-- -----------------------------------------------------------------------------
-- O ADR-014 exige "consentimento parental específico, com registro de prova".
-- Prova é o conjunto quem/quando/de onde MAIS a versão do texto consentido:
-- sem a versão, é impossível demonstrar depois A QUE a pessoa consentiu, e o
-- registro perde o valor jurídico que é a razão de existir.
CREATE TABLE parental_consents (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id      BIGINT       NOT NULL,
    child_id       BIGINT       NOT NULL,

    granted_by_member_id BIGINT NOT NULL,

    consent_version VARCHAR(40) NOT NULL,   -- ex.: "checkin-v1-2026-08"
    granted_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    granted_ip     INET,
    user_agent     VARCHAR(300),

    -- Consentimento é revogável por lei. Revogar não apaga a linha: a prova de
    -- que houve consentimento no passado é o que protege o tratamento já feito.
    revoked_at     TIMESTAMPTZ,

    CONSTRAINT fk_consents_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_consents_child FOREIGN KEY (child_id)
        REFERENCES children (id) ON DELETE RESTRICT,
    CONSTRAINT fk_consents_member FOREIGN KEY (granted_by_member_id)
        REFERENCES members (id) ON DELETE RESTRICT
);

CREATE INDEX ix_consents_child ON parental_consents (child_id, granted_at DESC);

ALTER TABLE parental_consents ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_consents ON parental_consents
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- -----------------------------------------------------------------------------
-- child_access_log — auditoria de LEITURA
-- -----------------------------------------------------------------------------
-- Auditoria de leitura, não de escrita — e essa é a diferença que importa. O
-- que se quer poder responder depois é "quem OLHOU a ficha desta criança", que
-- nenhum log de alteração responde.
--
-- Sem FK para `children`: o log precisa sobreviver à exclusão da ficha. Um
-- registro de auditoria que some junto com o objeto auditado não é auditoria.
CREATE TABLE child_access_log (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id      BIGINT       NOT NULL,
    child_id       BIGINT       NOT NULL,
    user_id        BIGINT       NOT NULL,

    action         VARCHAR(40)  NOT NULL,   -- read.detail, read.list, checkin, checkout
    occurred_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    ip_address     INET,
    correlation_id VARCHAR(40)
);

CREATE INDEX ix_child_access_child ON child_access_log (child_id, occurred_at DESC);

ALTER TABLE child_access_log ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_child_access ON child_access_log
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
