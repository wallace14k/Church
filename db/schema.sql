-- =============================================================================
-- Congrega — Schema PostgreSQL
-- =============================================================================
-- Convenções aplicadas em todo o arquivo:
--
--   * PK  : BIGINT GENERATED ALWAYS AS IDENTITY (restrição do briefing, Seção 2).
--           "ALWAYS" e não "BY DEFAULT" — impede que a aplicação injete IDs,
--           que é a origem clássica de colisão de sequence em carga de dados.
--   * public_id : UUID exposto na API para recursos que aparecem em URL.
--           Ver discordância D1 em docs/00-premissas.md. A PK continua numérica;
--           o UUID existe apenas para não expor contador sequencial ao mundo.
--   * Tempo : TIMESTAMPTZ sempre. Regra de negócio converte para
--           America/Sao_Paulo na borda; a persistência é UTC, sem exceção.
--   * Dinheiro : BIGINT em centavos. Nunca FLOAT, nunca DOUBLE.
--   * Exclusão : soft delete só onde há razão de negócio. FK sem ON DELETE CASCADE
--           em dado financeiro — apagar em cascata é como se perde histórico contábil.
--
-- Requisitos: PostgreSQL 15+ (usa UNIQUE NULLS NOT DISTINCT).
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS citext;      -- e-mail case-insensitive sem LOWER() em índice
CREATE EXTENSION IF NOT EXISTS pgcrypto;    -- gen_random_uuid()

-- =============================================================================
-- 1. TENANCY
-- =============================================================================

CREATE TABLE tenants (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id       UUID         NOT NULL DEFAULT gen_random_uuid(),
    name            VARCHAR(200) NOT NULL,
    slug            CITEXT       NOT NULL,
    document        VARCHAR(20),                -- CNPJ
    status          SMALLINT     NOT NULL DEFAULT 1,  -- 1=Trial 2=Active 3=Suspended 4=Canceled
    timezone        VARCHAR(50)  NOT NULL DEFAULT 'America/Sao_Paulo',
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    suspended_at    TIMESTAMPTZ,

    CONSTRAINT uq_tenants_public_id UNIQUE (public_id),
    CONSTRAINT uq_tenants_slug      UNIQUE (slug),
    CONSTRAINT ck_tenants_status    CHECK (status BETWEEN 1 AND 4)
);

COMMENT ON COLUMN tenants.slug IS
    'Identificador legível usado em convites e e-mails. CITEXT evita que "Betel" e "betel" coexistam.';

-- =============================================================================
-- 2. IDENTIDADE
-- =============================================================================
-- users é GLOBAL: não tem tenant_id. Esta é a decisão estrutural que sustenta o
-- requisito de primeira classe do briefing — a mesma pessoa pode ser membro de
-- uma igreja cliente E assinante Congrega+, ou apenas um dos dois.
-- Colocar tenant_id aqui obrigaria a duplicar a pessoa por igreja e destruiria
-- a assinatura pessoal, que não pertence a tenant nenhum.

CREATE TABLE users (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id         UUID        NOT NULL DEFAULT gen_random_uuid(),
    email             CITEXT      NOT NULL,
    full_name         VARCHAR(200) NOT NULL,
    phone             VARCHAR(20),
    email_verified    BOOLEAN     NOT NULL DEFAULT FALSE,
    status            SMALLINT    NOT NULL DEFAULT 1,  -- 1=Active 2=Blocked 3=Anonymized
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at     TIMESTAMPTZ,
    anonymized_at     TIMESTAMPTZ,                     -- LGPD Art. 18, VI

    CONSTRAINT uq_users_public_id UNIQUE (public_id),
    CONSTRAINT uq_users_email     UNIQUE (email),
    CONSTRAINT ck_users_status    CHECK (status BETWEEN 1 AND 3)
);

COMMENT ON COLUMN users.anonymized_at IS
    'Marca exercício do direito ao esquecimento. PII é sobrescrita; a linha permanece '
    'para que o ledger financeiro continue íntegro. Ver ADR-015.';

-- Credenciais em tabela separada de users: a maioria das queries lê perfil e nunca
-- precisa de material criptográfico. Separar reduz a chance de um SELECT * levar
-- hash de senha para um log ou para um DTO.
CREATE TABLE user_credentials (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id         BIGINT      NOT NULL,
    password_hash   TEXT,                            -- NULL = conta passwordless (padrão)
    mfa_secret_enc  BYTEA,                           -- TOTP, criptografado na aplicação
    mfa_enabled     BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_user_credentials_user FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT uq_user_credentials_user UNIQUE (user_id)
);

COMMENT ON COLUMN user_credentials.mfa_secret_enc IS
    'AES-256-GCM com chave no secret manager. O segredo TOTP em claro no banco '
    'anularia o propósito do segundo fator caso o banco vaze.';

-- OTP de verificação de e-mail / login passwordless
CREATE TABLE email_verification_codes (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id        BIGINT      NOT NULL,
    code_hash      BYTEA       NOT NULL,             -- HMAC-SHA256(código, pepper)
    purpose        SMALLINT    NOT NULL DEFAULT 1,   -- 1=Login 2=EmailChange 3=Recovery
    attempt_count  SMALLINT    NOT NULL DEFAULT 0,
    max_attempts   SMALLINT    NOT NULL DEFAULT 5,
    expires_at     TIMESTAMPTZ NOT NULL,
    consumed_at    TIMESTAMPTZ,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    request_ip     INET,

    CONSTRAINT fk_evc_user FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT ck_evc_attempts CHECK (attempt_count >= 0 AND max_attempts > 0)
);

COMMENT ON COLUMN email_verification_codes.code_hash IS
    'NUNCA o código em texto plano. HMAC com pepper — o espaço de 10^6 combinações '
    'é pequeno demais para hash sem chave: uma rainbow table cobre tudo em segundos.';

-- Índice parcial: só linhas ativas interessam. Códigos consumidos e expirados são
-- a maioria da tabela e nunca aparecem em query de caminho quente.
CREATE INDEX ix_evc_active
    ON email_verification_codes (user_id, expires_at DESC)
    WHERE consumed_at IS NULL;

-- Refresh tokens com rotação e family tracking (ver docs/02-autenticacao.md §5)
CREATE TABLE refresh_tokens (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id       BIGINT      NOT NULL,
    token_hash    BYTEA       NOT NULL,              -- SHA-256 do valor opaco
    family_id     UUID        NOT NULL,              -- agrupa a cadeia de rotação
    parent_id     BIGINT,                            -- token que originou este

    -- Tenant selecionado nesta sessão. NULL para assinante Congrega+ sem igreja.
    -- Guardado aqui para que a rotação reemita o access token com o mesmo tenant;
    -- sem isso, um usuário com duas igrejas cairia silenciosamente na errada a
    -- cada renovação. A troca explícita atualiza esta coluna preservando a family.
    selected_tenant_id BIGINT,
    issued_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at    TIMESTAMPTZ NOT NULL,
    used_at       TIMESTAMPTZ,                       -- preenchido na rotação
    revoked_at    TIMESTAMPTZ,
    revoked_reason SMALLINT,                         -- 1=Logout 2=ReuseDetected 3=Admin 4=PasswordChange
    device_label  VARCHAR(120),
    ip_address    INET,

    CONSTRAINT fk_refresh_user   FOREIGN KEY (user_id)   REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_refresh_parent FOREIGN KEY (parent_id) REFERENCES refresh_tokens (id) ON DELETE SET NULL,
    CONSTRAINT fk_refresh_tenant FOREIGN KEY (selected_tenant_id) REFERENCES tenants (id) ON DELETE SET NULL,
    CONSTRAINT uq_refresh_hash   UNIQUE (token_hash)
);

-- Busca por hash é o caminho quente do /auth/refresh — coberta pelo UNIQUE acima.
-- Este índice serve à revogação em massa da family quando reuso é detectado.
CREATE INDEX ix_refresh_family ON refresh_tokens (family_id)
    WHERE revoked_at IS NULL;
CREATE INDEX ix_refresh_user_active ON refresh_tokens (user_id)
    WHERE revoked_at IS NULL AND used_at IS NULL;

-- =============================================================================
-- 3. MEMBERSHIPS, PAPÉIS E PERMISSÕES
-- =============================================================================
-- memberships é a ponte N:N entre identidade global e tenant. É aqui que a
-- pergunta "esse usuário pode agir nessa igreja?" é respondida — nunca pela
-- claim do JWT sozinha.

CREATE TABLE memberships (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      BIGINT      NOT NULL,
    tenant_id    BIGINT      NOT NULL,
    status       SMALLINT    NOT NULL DEFAULT 1,   -- 1=Active 2=Inactive 3=Revoked
    joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    left_at      TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_membership_user   FOREIGN KEY (user_id)   REFERENCES users (id)   ON DELETE CASCADE,
    CONSTRAINT fk_membership_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_membership        UNIQUE (user_id, tenant_id),
    CONSTRAINT ck_membership_status CHECK (status BETWEEN 1 AND 3)
);

COMMENT ON TABLE memberships IS
    'Pessoa que muda de igreja NÃO vira um novo user: ganha uma segunda membership. '
    'O histórico da anterior é preservado com left_at, o que mantém a integridade dos '
    'registros financeiros e de presença já vinculados àquele tenant.';

-- Caminho quente: validar membership a cada requisição autenticada.
CREATE INDEX ix_membership_user_active ON memberships (user_id) WHERE status = 1;
CREATE INDEX ix_membership_tenant      ON memberships (tenant_id, status);

CREATE TABLE roles (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code        VARCHAR(50)  NOT NULL,     -- ChurchAdmin, Treasurer, CellLeader, Member
    name        VARCHAR(100) NOT NULL,
    is_system   BOOLEAN      NOT NULL DEFAULT TRUE,
    tenant_id   BIGINT,                    -- NULL = papel de sistema, disponível a todos

    CONSTRAINT fk_roles_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_roles_code   UNIQUE NULLS NOT DISTINCT (tenant_id, code)
);

COMMENT ON CONSTRAINT uq_roles_code ON roles IS
    'NULLS NOT DISTINCT (PG15+) faz o UNIQUE valer também para os papéis de sistema, '
    'onde tenant_id é NULL. Sem isso, o Postgres trataria cada NULL como distinto e '
    'permitiria dezenas de "ChurchAdmin" globais duplicados.';

CREATE TABLE permissions (
    id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code  VARCHAR(80)  NOT NULL,    -- members.read, giving.write, children.checkout
    name  VARCHAR(150) NOT NULL,

    CONSTRAINT uq_permissions_code UNIQUE (code)
);

CREATE TABLE role_permissions (
    role_id       BIGINT NOT NULL,
    permission_id BIGINT NOT NULL,

    PRIMARY KEY (role_id, permission_id),
    CONSTRAINT fk_rp_role FOREIGN KEY (role_id)       REFERENCES roles (id)       ON DELETE CASCADE,
    CONSTRAINT fk_rp_perm FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE
);

-- user_roles é ancorado em membership, não em user: papel só existe dentro de um
-- tenant. Ancorar em user_id permitiria "Tesoureiro" sem igreja — um papel órfão
-- que nenhuma policy conseguiria avaliar corretamente.
CREATE TABLE user_roles (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    membership_id BIGINT      NOT NULL,
    role_id       BIGINT      NOT NULL,
    granted_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    granted_by    BIGINT,

    CONSTRAINT fk_ur_membership FOREIGN KEY (membership_id) REFERENCES memberships (id) ON DELETE CASCADE,
    CONSTRAINT fk_ur_role       FOREIGN KEY (role_id)       REFERENCES roles (id)       ON DELETE RESTRICT,
    CONSTRAINT fk_ur_granted_by FOREIGN KEY (granted_by)    REFERENCES users (id)       ON DELETE SET NULL,
    CONSTRAINT uq_user_role     UNIQUE (membership_id, role_id)
);

-- ON DELETE RESTRICT em role_id é intencional: apagar um papel que ainda está
-- concedido a alguém deve falhar ruidosamente, não revogar acessos em silêncio.

CREATE INDEX ix_user_roles_membership ON user_roles (membership_id);

-- =============================================================================
-- 4. CATÁLOGO — PACKS E ITENS
-- =============================================================================
-- Resposta à pergunta do briefing ("pack é compra avulsa, incluso na assinatura,
-- ou ambos?"): AMBOS. O pack é a unidade de conteúdo; como ele é adquirido é
-- registrado em entitlements, não no próprio pack. Isso permite que o mesmo pack
-- seja vendido avulso, incluído em um plano e dado de cortesia — sem duplicação.

CREATE TABLE resource_packs (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id       UUID         NOT NULL DEFAULT gen_random_uuid(),
    slug            CITEXT       NOT NULL,
    title           VARCHAR(200) NOT NULL,
    description     TEXT,
    pack_type       SMALLINT     NOT NULL,  -- 1=Sermao 2=Arte 3=Campanha 4=Ambiente 5=Midia 6=SomLuz 7=Curso 8=EBook
    cover_key       VARCHAR(500),
    price_cents     BIGINT,                 -- NULL = não vendido avulso
    currency        CHAR(3)      NOT NULL DEFAULT 'BRL',
    is_published    BOOLEAN      NOT NULL DEFAULT FALSE,
    published_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_packs_public_id UNIQUE (public_id),
    CONSTRAINT uq_packs_slug      UNIQUE (slug),
    CONSTRAINT ck_packs_price     CHECK (price_cents IS NULL OR price_cents > 0),
    CONSTRAINT ck_packs_type      CHECK (pack_type BETWEEN 1 AND 8)
);

-- Listagem do catálogo filtra por publicado e ordena por data. Índice parcial
-- porque rascunho não publicado nunca aparece na vitrine.
CREATE INDEX ix_packs_published ON resource_packs (pack_type, published_at DESC)
    WHERE is_published = TRUE;

CREATE TABLE pack_items (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    resource_pack_id  BIGINT       NOT NULL,
    title             VARCHAR(200) NOT NULL,
    item_type         SMALLINT     NOT NULL,  -- 1=Video 2=PDF 3=EPUB 4=Zip 5=Imagem 6=Audio
    storage_provider  SMALLINT     NOT NULL,  -- 1=R2 2=VideoProvider 3=SupabaseStorage
    storage_key       VARCHAR(500) NOT NULL,  -- caminho no bucket ou ID do vídeo
    size_bytes        BIGINT,
    duration_seconds  INTEGER,                -- vídeo e áudio
    sort_order        INTEGER      NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_pack_items_pack FOREIGN KEY (resource_pack_id)
        REFERENCES resource_packs (id) ON DELETE CASCADE,
    CONSTRAINT ck_pack_items_size CHECK (size_bytes IS NULL OR size_bytes > 0)
);

CREATE INDEX ix_pack_items_pack ON pack_items (resource_pack_id, sort_order);

-- =============================================================================
-- 5. PLANOS E ASSINATURAS
-- =============================================================================

CREATE TABLE plans (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id         UUID         NOT NULL DEFAULT gen_random_uuid(),
    code              VARCHAR(50)  NOT NULL,   -- chms_basic, premium_monthly, premium_annual
    name              VARCHAR(120) NOT NULL,
    audience          SMALLINT     NOT NULL,   -- 1=Tenant (ChMS B2B)  2=User (Congrega+ B2C)
    billing_period    SMALLINT     NOT NULL,   -- 1=Mensal 2=Anual
    price_cents       BIGINT       NOT NULL,
    currency          CHAR(3)      NOT NULL DEFAULT 'BRL',
    trial_days        SMALLINT     NOT NULL DEFAULT 0,
    grace_days        SMALLINT     NOT NULL DEFAULT 7,
    apple_product_id  VARCHAR(120),            -- SKU correspondente no App Store Connect
    google_product_id VARCHAR(120),            -- SKU correspondente no Play Console
    is_active         BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT uq_plans_public_id UNIQUE (public_id),
    CONSTRAINT uq_plans_code      UNIQUE (code),
    CONSTRAINT ck_plans_audience  CHECK (audience IN (1, 2)),
    CONSTRAINT ck_plans_price     CHECK (price_cents >= 0),
    CONSTRAINT ck_plans_grace     CHECK (grace_days BETWEEN 0 AND 30)
);

COMMENT ON COLUMN plans.audience IS
    'Separa os dois modelos de receita. audience=1 é cobrado do tenant via Abacate.pay '
    '(serviço B2B, fora do IAP). audience=2 é cobrado do indivíduo e, dentro de app, '
    'precisa passar por IAP/Play Billing — daí as colunas apple_product_id e google_product_id. '
    'Ver ADR-009.';

-- Quais packs cada plano libera. É a ponte que permite ao entitlement de assinatura
-- cobrir conteúdo publicado DEPOIS da contratação, sem backfill.
CREATE TABLE plan_packs (
    plan_id          BIGINT      NOT NULL,
    resource_pack_id BIGINT      NOT NULL,
    added_at         TIMESTAMPTZ NOT NULL DEFAULT now(),

    PRIMARY KEY (plan_id, resource_pack_id),
    CONSTRAINT fk_pp_plan FOREIGN KEY (plan_id)          REFERENCES plans (id)          ON DELETE CASCADE,
    CONSTRAINT fk_pp_pack FOREIGN KEY (resource_pack_id) REFERENCES resource_packs (id) ON DELETE CASCADE
);

CREATE INDEX ix_plan_packs_pack ON plan_packs (resource_pack_id);

CREATE TABLE subscriptions (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id           UUID        NOT NULL DEFAULT gen_random_uuid(),
    plan_id             BIGINT      NOT NULL,

    -- Exatamente um dos dois é preenchido: assinatura de igreja OU de pessoa.
    -- O CHECK abaixo é o que impede o estado impossível de existirem os dois.
    tenant_id           BIGINT,
    user_id             BIGINT,

    status              SMALLINT    NOT NULL,  -- 1=Pending 2=Active 3=PastDue 4=Grace 5=Canceled 6=Expired
    source              SMALLINT    NOT NULL,  -- 1=AbacatePay 2=AppleAppStore 3=GooglePlay 4=Courtesy
    external_id         VARCHAR(200),          -- ID da assinatura no provedor externo

    current_period_start TIMESTAMPTZ NOT NULL,
    current_period_end   TIMESTAMPTZ NOT NULL,  -- coluna que o motor de retenção varre
    grace_until          TIMESTAMPTZ,
    trial_ends_at        TIMESTAMPTZ,
    canceled_at          TIMESTAMPTZ,
    cancel_at_period_end BOOLEAN     NOT NULL DEFAULT FALSE,

    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_sub_plan   FOREIGN KEY (plan_id)   REFERENCES plans (id)   ON DELETE RESTRICT,
    CONSTRAINT fk_sub_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT fk_sub_user   FOREIGN KEY (user_id)   REFERENCES users (id)   ON DELETE CASCADE,
    CONSTRAINT uq_sub_public_id UNIQUE (public_id),

    CONSTRAINT ck_sub_owner CHECK (
        (tenant_id IS NOT NULL AND user_id IS NULL) OR
        (tenant_id IS NULL AND user_id IS NOT NULL)
    ),
    CONSTRAINT ck_sub_status CHECK (status BETWEEN 1 AND 6),
    CONSTRAINT ck_sub_source CHECK (source BETWEEN 1 AND 4),
    CONSTRAINT ck_sub_period CHECK (current_period_end > current_period_start)
);

COMMENT ON CONSTRAINT ck_sub_owner ON subscriptions IS
    'Uma assinatura pertence a uma igreja OU a uma pessoa, nunca a ambas. Sem este '
    'CHECK, o modelo permitiria uma linha ambígua que nenhuma regra de cobrança '
    'saberia interpretar.';

-- Assinatura ativa por dono. UNIQUE parcial impede a dupla cobrança que surge de
-- webhook duplicado combinado com retry de checkout.
CREATE UNIQUE INDEX uq_sub_active_user ON subscriptions (user_id)
    WHERE status IN (1, 2, 3, 4) AND user_id IS NOT NULL;
CREATE UNIQUE INDEX uq_sub_active_tenant ON subscriptions (tenant_id)
    WHERE status IN (1, 2, 3, 4) AND tenant_id IS NOT NULL;

-- ÍNDICE CRÍTICO DO MOTOR DE RETENÇÃO (entregável 6.5).
-- A varredura filtra por status e faixa de current_period_end. Índice composto
-- nessa ordem permite varrer apenas as assinaturas na janela, em vez da tabela toda.
CREATE INDEX ix_sub_retention ON subscriptions (status, current_period_end)
    WHERE status IN (2, 3, 4);

CREATE INDEX ix_sub_external ON subscriptions (source, external_id)
    WHERE external_id IS NOT NULL;

-- Histórico imutável de transições. É a fonte para auditoria e para responder
-- "por que este usuário perdeu acesso no dia 12?".
CREATE TABLE subscription_events (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    subscription_id BIGINT      NOT NULL,
    event_type      SMALLINT    NOT NULL,  -- 1=Created 2=Activated 3=Renewed 4=PaymentFailed
                                           -- 5=EnteredGrace 6=Canceled 7=Expired 8=Reactivated
    from_status     SMALLINT,
    to_status       SMALLINT    NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    correlation_id  VARCHAR(40),
    payload         JSONB,

    CONSTRAINT fk_sub_events_sub FOREIGN KEY (subscription_id)
        REFERENCES subscriptions (id) ON DELETE CASCADE
);

CREATE INDEX ix_sub_events_sub ON subscription_events (subscription_id, occurred_at DESC);

-- =============================================================================
-- 6. PAGAMENTOS E WEBHOOKS
-- =============================================================================

CREATE TABLE payments (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id         UUID        NOT NULL DEFAULT gen_random_uuid(),
    subscription_id   BIGINT,                        -- NULL em compra avulsa de pack
    user_id           BIGINT,
    tenant_id         BIGINT,
    resource_pack_id  BIGINT,                        -- preenchido em compra avulsa

    amount_cents      BIGINT      NOT NULL,
    currency          CHAR(3)     NOT NULL DEFAULT 'BRL',
    status            SMALLINT    NOT NULL,  -- 1=Pending 2=Paid 3=Failed 4=Refunded 5=Chargeback
    method            SMALLINT,              -- 1=Pix 2=CreditCard 3=IAP 4=Boleto
    source            SMALLINT    NOT NULL,  -- espelha subscriptions.source

    gateway_charge_id VARCHAR(200),
    idempotency_key   VARCHAR(100) NOT NULL,

    paid_at           TIMESTAMPTZ,
    failed_at         TIMESTAMPTZ,
    failure_reason    VARCHAR(300),
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_pay_sub    FOREIGN KEY (subscription_id)  REFERENCES subscriptions (id)  ON DELETE RESTRICT,
    CONSTRAINT fk_pay_user   FOREIGN KEY (user_id)          REFERENCES users (id)          ON DELETE RESTRICT,
    CONSTRAINT fk_pay_tenant FOREIGN KEY (tenant_id)        REFERENCES tenants (id)        ON DELETE RESTRICT,
    CONSTRAINT fk_pay_pack   FOREIGN KEY (resource_pack_id) REFERENCES resource_packs (id) ON DELETE RESTRICT,

    CONSTRAINT uq_pay_public_id       UNIQUE (public_id),
    CONSTRAINT uq_pay_idempotency_key UNIQUE (idempotency_key),
    CONSTRAINT ck_pay_amount          CHECK (amount_cents > 0),
    CONSTRAINT ck_pay_status          CHECK (status BETWEEN 1 AND 5)
);

COMMENT ON CONSTRAINT uq_pay_idempotency_key ON payments IS
    'A garantia de que duplo clique, retry de rede ou reenvio de requisição não geram '
    'duas cobranças. A unicidade é do BANCO — verificação prévia em código é race '
    'condition sob concorrência (skill de segurança, §14).';

COMMENT ON COLUMN payments.user_id IS
    'Mantido mesmo após anonimização do titular (LGPD). O ledger preserva o vínculo '
    'estrutural; users perde a PII. Por isso as FKs aqui são RESTRICT, nunca CASCADE.';

CREATE INDEX ix_pay_sub      ON payments (subscription_id, created_at DESC);
CREATE INDEX ix_pay_tenant   ON payments (tenant_id, created_at DESC) WHERE tenant_id IS NOT NULL;
CREATE INDEX ix_pay_gateway  ON payments (gateway_charge_id) WHERE gateway_charge_id IS NOT NULL;

-- Idempotência de webhook. O evento cru é persistido ANTES de qualquer
-- processamento — perder um webhook de pagamento é perder dinheiro.
CREATE TABLE payment_webhooks (
    id                 BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider           SMALLINT     NOT NULL,  -- 1=AbacatePay 2=Apple 3=Google
    provider_event_id  VARCHAR(200) NOT NULL,
    event_type         VARCHAR(100) NOT NULL,
    payload            JSONB        NOT NULL,  -- corpo cru, como recebido
    signature_valid    BOOLEAN      NOT NULL,
    received_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at       TIMESTAMPTZ,
    process_attempts   SMALLINT     NOT NULL DEFAULT 0,
    last_error         TEXT,
    correlation_id     VARCHAR(40),

    CONSTRAINT uq_webhook_event UNIQUE (provider, provider_event_id)
);

COMMENT ON CONSTRAINT uq_webhook_event ON payment_webhooks IS
    'O coração da idempotência. Webhook duplicado colide aqui e o INSERT falha — '
    'a API responde 200 e nada é processado duas vezes. Isso é uma garantia do banco, '
    'não uma verificação em código que perde a corrida sob concorrência.';

-- Fila de processamento: o worker busca não processados com FOR UPDATE SKIP LOCKED.
CREATE INDEX ix_webhook_pending ON payment_webhooks (received_at)
    WHERE processed_at IS NULL;

-- =============================================================================
-- 7. ENTITLEMENTS — a resolução única de acesso
-- =============================================================================
-- Esta é a tabela mais importante do modelo. Ela responde à pergunta do briefing:
-- como acesso vindo de origens diferentes (assinatura, compra avulsa, cortesia, IAP)
-- é resolvido por um único caminho.
--
-- A resposta: o entitlement guarda O QUE foi liberado e DE ONDE veio, em duas
-- granularidades possíveis:
--
--   plan_id preenchido          → acesso a tudo que o plano cobre (via plan_packs).
--                                  Cobre conteúdo publicado DEPOIS da contratação,
--                                  sem precisar de backfill.
--   resource_pack_id preenchido → acesso a um pack específico (compra avulsa,
--                                  cortesia, ou item único comprado por IAP).
--
-- Autorização NUNCA pergunta "esse usuário pagou?" nem lê a claim do JWT.
-- Pergunta "existe entitlement válido agora?" — e a resposta vale igual para as
-- quatro origens.

CREATE TABLE entitlements (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id           BIGINT      NOT NULL,

    plan_id           BIGINT,
    resource_pack_id  BIGINT,

    source            SMALLINT    NOT NULL,  -- 1=Subscription 2=OneOffPurchase 3=Courtesy 4=IAP
    source_subscription_id BIGINT,
    source_payment_id      BIGINT,

    granted_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at        TIMESTAMPTZ,           -- NULL = perpétuo (compra avulsa)
    revoked_at        TIMESTAMPTZ,
    revoked_reason    SMALLINT,              -- 1=SubscriptionEnded 2=Refund 3=Chargeback 4=Admin

    granted_by        BIGINT,                -- quem concedeu, em cortesia
    note              VARCHAR(300),

    CONSTRAINT fk_ent_user    FOREIGN KEY (user_id)          REFERENCES users (id)          ON DELETE CASCADE,
    CONSTRAINT fk_ent_plan    FOREIGN KEY (plan_id)          REFERENCES plans (id)          ON DELETE RESTRICT,
    CONSTRAINT fk_ent_pack    FOREIGN KEY (resource_pack_id) REFERENCES resource_packs (id) ON DELETE CASCADE,
    CONSTRAINT fk_ent_sub     FOREIGN KEY (source_subscription_id) REFERENCES subscriptions (id) ON DELETE SET NULL,
    CONSTRAINT fk_ent_payment FOREIGN KEY (source_payment_id)      REFERENCES payments (id)      ON DELETE SET NULL,
    CONSTRAINT fk_ent_granter FOREIGN KEY (granted_by)             REFERENCES users (id)         ON DELETE SET NULL,

    CONSTRAINT ck_ent_scope CHECK (
        (plan_id IS NOT NULL AND resource_pack_id IS NULL) OR
        (plan_id IS NULL AND resource_pack_id IS NOT NULL)
    ),
    CONSTRAINT ck_ent_source CHECK (source BETWEEN 1 AND 4)
);

COMMENT ON CONSTRAINT ck_ent_scope ON entitlements IS
    'Um entitlement é de plano OU de pack, nunca dos dois. O estado ambíguo tornaria '
    'a query de resolução não determinística.';

-- Caminho MAIS quente do sistema: resolver acesso a cada abertura de conteúdo.
-- Índice parcial só com entitlements vigentes — revogados nunca são consultados.
CREATE INDEX ix_ent_user_active ON entitlements (user_id, expires_at)
    WHERE revoked_at IS NULL;
CREATE INDEX ix_ent_pack   ON entitlements (resource_pack_id) WHERE revoked_at IS NULL;
CREATE INDEX ix_ent_source_sub ON entitlements (source_subscription_id)
    WHERE source_subscription_id IS NOT NULL;

-- -----------------------------------------------------------------------------
-- Query canônica de autorização — o usuário :userId pode acessar o pack :packId?
-- Uma única consulta cobre as quatro origens de acesso.
-- -----------------------------------------------------------------------------
--   SELECT EXISTS (
--       SELECT 1
--         FROM entitlements e
--         LEFT JOIN plan_packs pp ON pp.plan_id = e.plan_id
--        WHERE e.user_id = :userId
--          AND e.revoked_at IS NULL
--          AND (e.expires_at IS NULL OR e.expires_at > now())
--          AND (e.resource_pack_id = :packId OR pp.resource_pack_id = :packId)
--   );
-- -----------------------------------------------------------------------------

-- Registro de cada URL assinada emitida. Serve a três propósitos: auditoria,
-- limite de download por entitlement e detecção de abuso (mesmo usuário, muitos IPs).
CREATE TABLE download_grants (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id      UUID        NOT NULL DEFAULT gen_random_uuid(),
    user_id        BIGINT      NOT NULL,
    pack_item_id   BIGINT      NOT NULL,
    entitlement_id BIGINT      NOT NULL,   -- qual direito justificou a emissão

    issued_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at     TIMESTAMPTZ NOT NULL,   -- TTL curto: 60 a 300 segundos
    consumed_at    TIMESTAMPTZ,
    ip_address     INET,
    user_agent     VARCHAR(400),

    CONSTRAINT fk_dg_user FOREIGN KEY (user_id)        REFERENCES users (id)        ON DELETE CASCADE,
    CONSTRAINT fk_dg_item FOREIGN KEY (pack_item_id)   REFERENCES pack_items (id)   ON DELETE CASCADE,
    CONSTRAINT fk_dg_ent  FOREIGN KEY (entitlement_id) REFERENCES entitlements (id) ON DELETE CASCADE,
    CONSTRAINT uq_dg_public_id UNIQUE (public_id),
    CONSTRAINT ck_dg_ttl       CHECK (expires_at > issued_at)
);

COMMENT ON COLUMN download_grants.entitlement_id IS
    'Amarra a emissão ao direito que a justificou. Em auditoria, responde não apenas '
    '"quem baixou" mas "com base em qual direito" — essencial quando um reembolso '
    'obriga a revisar acessos concedidos.';

-- Suporta o limite por janela e a detecção de anomalia.
CREATE INDEX ix_dg_user_recent ON download_grants (user_id, issued_at DESC);
CREATE INDEX ix_dg_item        ON download_grants (pack_item_id, issued_at DESC);

-- =============================================================================
-- 8. NOTIFICAÇÕES E OUTBOX
-- =============================================================================

-- Outbox transacional: evento de domínio gravado na MESMA transação da mudança
-- de estado. Elimina a janela entre "commit no banco" e "publicar mensagem" onde
-- se perdem notificações (skill de segurança, §26).
CREATE TABLE outbox_messages (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    message_type    VARCHAR(200) NOT NULL,
    payload         JSONB        NOT NULL,
    occurred_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at    TIMESTAMPTZ,
    attempts        SMALLINT     NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ  NOT NULL DEFAULT now(),
    last_error      TEXT,
    correlation_id  VARCHAR(40),

    CONSTRAINT ck_outbox_attempts CHECK (attempts >= 0)
);

-- Índice parcial que sustenta o SELECT ... FOR UPDATE SKIP LOCKED do dispatcher.
-- A tabela cresce indefinidamente; sem o WHERE, o índice cresceria junto e a
-- varredura ficaria mais lenta a cada semana de operação.
CREATE INDEX ix_outbox_pending ON outbox_messages (next_attempt_at)
    WHERE processed_at IS NULL;

-- Fila de notificações prontas para envio, já resolvidas por canal e destinatário.
CREATE TABLE notification_queue (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id         BIGINT       NOT NULL,
    tenant_id       BIGINT,
    channel         SMALLINT     NOT NULL,  -- 1=Email 2=Push 3=InAppBanner
    template_code   VARCHAR(80)  NOT NULL,  -- retention.d15, retention.grace.d3, checkin.alert
    payload         JSONB        NOT NULL,

    -- Chave de deduplicação. É ESTA constraint que garante o requisito do briefing
    -- ("um mesmo usuário não recebe o mesmo alerta duas vezes"), e não a lógica do
    -- worker. Locks distribuídos falham; UNIQUE no banco não.
    dedupe_key      VARCHAR(200) NOT NULL,

    scheduled_for   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    sent_at         TIMESTAMPTZ,
    failed_at       TIMESTAMPTZ,
    attempts        SMALLINT     NOT NULL DEFAULT 0,
    last_error      TEXT,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    correlation_id  VARCHAR(40),

    CONSTRAINT fk_nq_user   FOREIGN KEY (user_id)   REFERENCES users (id)   ON DELETE CASCADE,
    CONSTRAINT fk_nq_tenant FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_nq_dedupe UNIQUE (dedupe_key),
    CONSTRAINT ck_nq_channel CHECK (channel BETWEEN 1 AND 3)
);

COMMENT ON COLUMN notification_queue.dedupe_key IS
    'Formato do motor de retenção: "retention:{subscriptionId}:{periodEndDate}:{window}". '
    'Incluir periodEnd é essencial — sem ele, a assinatura renovada nunca receberia '
    'alerta de novo, porque a chave do ciclo anterior já ocuparia o lugar.';

CREATE INDEX ix_nq_pending ON notification_queue (scheduled_for)
    WHERE sent_at IS NULL AND failed_at IS NULL;
CREATE INDEX ix_nq_user ON notification_queue (user_id, created_at DESC);

-- =============================================================================
-- 9. AUDITORIA E EVENTOS DE SEGURANÇA
-- =============================================================================

CREATE TABLE audit_log (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_user_id   BIGINT,
    tenant_id       BIGINT,
    action          VARCHAR(100) NOT NULL,   -- member.role_changed, child.record_viewed
    target_type     VARCHAR(60)  NOT NULL,
    target_id       BIGINT,
    result          SMALLINT     NOT NULL,   -- 1=Success 2=Denied 3=Error
    ip_address      INET,
    user_agent      VARCHAR(400),
    correlation_id  VARCHAR(40),
    metadata        JSONB,
    occurred_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_audit_actor  FOREIGN KEY (actor_user_id) REFERENCES users (id)   ON DELETE SET NULL,
    CONSTRAINT fk_audit_tenant FOREIGN KEY (tenant_id)     REFERENCES tenants (id) ON DELETE SET NULL
);

COMMENT ON TABLE audit_log IS
    'Append-only. A role da aplicação recebe apenas INSERT e SELECT — sem UPDATE e '
    'sem DELETE. Log de auditoria alterável não é log de auditoria (skill, §29).';

CREATE INDEX ix_audit_tenant ON audit_log (tenant_id, occurred_at DESC);
CREATE INDEX ix_audit_actor  ON audit_log (actor_user_id, occurred_at DESC);
CREATE INDEX ix_audit_target ON audit_log (target_type, target_id, occurred_at DESC);

CREATE TABLE security_events (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id        BIGINT,
    event_type     VARCHAR(80) NOT NULL,  -- OtpMaxAttempts, RefreshTokenReuseDetected,
                                          -- WebhookSignatureInvalid, ChildCheckoutCodeInvalid
    severity       SMALLINT    NOT NULL,  -- 1=Info 2=Warning 3=Critical
    ip_address     INET,
    metadata       JSONB,
    correlation_id VARCHAR(40),
    occurred_at    TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_secev_user FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_secev_severity CHECK (severity BETWEEN 1 AND 3)
);

CREATE INDEX ix_secev_type ON security_events (event_type, occurred_at DESC);
CREATE INDEX ix_secev_critical ON security_events (occurred_at DESC) WHERE severity = 3;

-- =============================================================================
-- 10. ROW LEVEL SECURITY — rede de segurança (ADR-006)
-- =============================================================================
-- A aplicação continua sendo a AUTORIDADE de isolamento (Global Query Filters do
-- EF Core). O RLS existe para o caso em que um filtro é esquecido: em vez de
-- vazamento cross-tenant, o resultado é conjunto vazio.
--
-- O contexto chega por SET LOCAL, emitido pelo interceptor de conexão do EF Core
-- no início de cada transação. SET LOCAL é transacional e, portanto, seguro sob
-- transaction pooling do Supavisor — o valor não sobrevive para a próxima
-- requisição que reutilizar a conexão física.

ALTER TABLE memberships        ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscriptions      ENABLE ROW LEVEL SECURITY;
ALTER TABLE payments           ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_queue ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_log          ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_memberships ON memberships
    FOR ALL
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

CREATE POLICY tenant_isolation_subscriptions ON subscriptions
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT
        OR user_id = NULLIF(current_setting('app.user_id', TRUE), '')::BIGINT
    );

CREATE POLICY tenant_isolation_payments ON payments
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT
        OR user_id = NULLIF(current_setting('app.user_id', TRUE), '')::BIGINT
    );

CREATE POLICY tenant_isolation_notifications ON notification_queue
    FOR ALL
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT
        OR user_id  = NULLIF(current_setting('app.user_id', TRUE), '')::BIGINT
    );

CREATE POLICY tenant_isolation_audit ON audit_log
    FOR ALL
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- NULLIF(..., '') é necessário porque current_setting com missing_ok=TRUE devolve
-- string vazia, não NULL, quando a variável não foi definida — e ''::BIGINT lança
-- erro. Com NULLIF, a comparação vira "= NULL", que é falsa: fail closed.
-- Um bug de configuração nega acesso em vez de liberar tudo.

-- -----------------------------------------------------------------------------
-- Roles de banco. Ver ADR-006.
-- -----------------------------------------------------------------------------
-- CREATE ROLE congrega_app    LOGIN;  -- API: RLS aplicado
-- CREATE ROLE congrega_worker LOGIN BYPASSRLS;  -- jobs que cruzam tenants
--
-- A credencial de congrega_worker só é injetada nos deployments de worker.
-- Um teste de integração verifica que o pod da API não lê linha de outro tenant
-- mesmo com o Global Query Filter desabilitado — é assim que se prova que a rede
-- de segurança está de fato armada.
