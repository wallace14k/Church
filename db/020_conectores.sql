-- Conectores: os serviços externos que cada igreja configura.
--
-- ============================================================================
-- Por que a credencial mora no banco, e o que a protege lá
-- ============================================================================
--
-- Chave de API costuma — e deve — morar no secret manager. Aqui não pode: cada
-- igreja tem a **sua** conta de e-mail e o **seu** bot, e uma variável de
-- ambiente por tenant significaria republicar a aplicação toda vez que uma
-- igreja nova assinasse.
--
-- O que muda é a proteção. A coluna guarda AES-256-GCM cifrado **na aplicação**,
-- com chave que vive no secret manager e nunca no banco. As consequências, todas
-- deliberadas:
--
--   * Um dump do banco não entrega credencial nenhuma. O DBA lê bytes.
--   * Não dá para consultar por segredo — o nonce é novo a cada gravação. É
--     correto: ninguém procura conector por senha, e um esquema determinístico
--     revelaria quais igrejas usam a mesma.
--   * Rotacionar a chave exige recifrar as linhas. É o preço, e é conhecido.
--
-- **A chave é de uso próprio, separada da do check-in infantil.** As duas têm
-- ciclos de vida independentes: rotacionar a chave dos conectores porque um
-- integrador viu o secret não pode invalidar a ficha de alergia de nenhuma
-- criança. O `ChildSafetyOptions` já registra esse mesmo raciocínio para o par
-- que ele guarda.

CREATE TABLE tenant_connectors (
    id                  BIGINT       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id           UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id           BIGINT       NOT NULL,

    -- 1=Smtp 2=Telegram 3=GoogleDrive
    kind                SMALLINT     NOT NULL,

    is_enabled          BOOLEAN      NOT NULL DEFAULT TRUE,

    -- Configuração que NÃO é segredo: host e porta, id do chat, id da pasta.
    --
    -- JSONB e não colunas: host do SMTP, chat do Telegram e pasta do Drive não
    -- têm nada em comum. Uma coluna para cada produziria uma tabela em que dois
    -- terços dos campos são sempre nulos, e um quarto conector exigiria
    -- migration para funcionar.
    settings            JSONB        NOT NULL DEFAULT '{}'::JSONB,

    -- O segredo, cifrado na aplicação. Ver o cabeçalho.
    secret_enc          BYTEA        NOT NULL,

    last_tested_at      TIMESTAMPTZ,
    last_test_succeeded BOOLEAN,
    last_test_message   VARCHAR(300),

    created_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_tenant_connectors_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    CONSTRAINT uq_tenant_connectors_public_id UNIQUE (public_id),

    -- **Um conector de cada tipo por igreja.**
    --
    -- É o que dá sentido a "o e-mail desta igreja": com dois SMTP cadastrados,
    -- qualquer envio teria de escolher um, e a escolha seria arbitrária. A
    -- constraint torna a pergunta impossível em vez de respondê-la mal.
    CONSTRAINT uq_tenant_connectors_tipo UNIQUE (tenant_id, kind),

    CONSTRAINT ck_tenant_connectors_tipo CHECK (kind BETWEEN 1 AND 3),

    -- **A rede contra o pior erro possível: senha em texto claro na coluna.**
    --
    -- AES-GCM como este projeto grava é `nonce(12) || tag(16) || texto cifrado`,
    -- então qualquer segredo real tem no mínimo 29 bytes. Uma senha colada
    -- direto teria menos, e o banco a recusa. Não é criptografia — é o alarme
    -- que dispara se alguém contornar o cifrador na aplicação.
    CONSTRAINT ck_tenant_connectors_segredo_cifrado
        CHECK (octet_length(secret_enc) >= 29),

    -- Configuração é objeto, não lista nem número. Sem isto, `settings` aceita
    -- `'[]'` e `'3'`, e a aplicação quebra ao ler uma chave de um array.
    CONSTRAINT ck_tenant_connectors_settings_objeto
        CHECK (jsonb_typeof(settings) = 'object'),

    -- Mensagem de teste sem data de teste é um diagnóstico sem quando: ou os
    -- três campos descrevem a mesma tentativa, ou nenhum descreve nada.
    CONSTRAINT ck_tenant_connectors_teste_coerente
        CHECK (
            (last_tested_at IS NULL AND last_test_succeeded IS NULL)
            OR (last_tested_at IS NOT NULL AND last_test_succeeded IS NOT NULL)
        )
);

CREATE INDEX ix_tenant_connectors_tenant ON tenant_connectors (tenant_id);

ALTER TABLE tenant_connectors ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_tenant_connectors ON tenant_connectors
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- ---------------------------------------------------------------------------
-- Permissão própria
-- ---------------------------------------------------------------------------
--
-- Quem administra conectores digita a senha da conta de e-mail da igreja — e a
-- conta de e-mail costuma ser a que recupera todas as outras senhas dela. Isso
-- não pode vir junto de `members.write`.
--
-- `ChurchAdmin` recebe: é a configuração da igreja, não do caixa nem do
-- cadastro. `Treasurer` fica de fora — ele movimenta dinheiro, não integra
-- sistemas.
INSERT INTO permissions (code, name)
VALUES ('connectors.manage', 'Configurar integrações da igreja')
ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.code = 'ChurchAdmin'
  AND r.tenant_id IS NULL   -- papel de sistema; homônimo de um tenant não herda
  AND p.code = 'connectors.manage'
ON CONFLICT (role_id, permission_id) DO NOTHING;
