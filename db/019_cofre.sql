-- Cofre da igreja: onde o dinheiro do caixa fica guardado.
--
-- ============================================================================
-- A decisão que organiza esta tabela: cofre NÃO é entrada nem saída
-- ============================================================================
--
-- Guardar R$ 5.000 do caixa no cofre é a mesma nota de R$ 5.000 mudando de
-- gaveta. Nada entrou, nada saiu, o patrimônio da igreja é o mesmo antes e
-- depois.
--
-- Se esse movimento virasse `giving_entries`, o fechamento do mês passaria a
-- afirmar que a igreja teve R$ 5.000 de despesa — e a prestação de contas
-- mostraria um prejuízo que não existiu. Guardar dinheiro no cofre viraria,
-- no relatório, o mesmo que gastá-lo.
--
-- Por isso os movimentos de cofre vivem **em tabela própria, fora do
-- fechamento**. O saldo do cofre é uma informação de guarda, não de resultado.
--
-- ============================================================================
-- A segunda decisão: o saldo não fica negativo, e quem garante é o CHECK
-- ============================================================================
--
-- Retirar do cofre mais do que ele tem é impossível fisicamente, e o sistema
-- precisa dizer o mesmo. A tentação é `if (saldo >= valor)` antes de inserir —
-- e sob duas abas abertas isso tem janela: as duas leem R$ 100, as duas
-- aprovam R$ 100, e o cofre fica com −R$ 100.
--
-- Aqui cada movimento **grava o saldo que resulta dele**, com
-- `CHECK (balance_after_cents >= 0)`, e a ordem é serializada por
-- `UNIQUE (tenant_id, sequence_number)`. Duas inserções concorrentes calculam o
-- mesmo número de sequência a partir da mesma leitura, e a segunda é recusada
-- pelo índice — não por uma verificação que pode chegar tarde.
--
-- O efeito colateral é bem-vindo: a coluna de saldo torna o extrato do cofre
-- conferível linha a linha, como o extrato de um banco.

CREATE TABLE vault_movements (
    id                   BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id            UUID        NOT NULL DEFAULT gen_random_uuid(),
    tenant_id            BIGINT      NOT NULL,

    -- Posição do movimento no extrato do cofre desta igreja. 1, 2, 3...
    sequence_number      BIGINT      NOT NULL,

    -- 1=Deposito (caixa -> cofre)  2=Retirada (cofre -> caixa)
    direction            SMALLINT    NOT NULL,

    -- Centavos, sempre positivo. O sentido vem de `direction`, nunca do sinal —
    -- é a mesma escolha de `giving_entries`, e pelo mesmo motivo: valor negativo
    -- some numa soma distraída.
    amount_cents         BIGINT      NOT NULL,

    -- Saldo do cofre DEPOIS deste movimento.
    balance_after_cents  BIGINT      NOT NULL,

    -- De onde saiu, ou para onde voltou. Opcional: uma igreja com caixa único
    -- não precisa cadastrar conta para usar o cofre.
    account_id           BIGINT,

    occurred_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes                VARCHAR(500),

    -- Quem mexeu no cofre. **Não é opcional na prática**: movimento de cofre
    -- sem autor é exatamente o que uma auditoria procura. Nulo só existe para
    -- não travar rotina de sistema sem usuário.
    performed_by_user_id BIGINT,

    created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT fk_vault_movements_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- RESTRICT, como todo vínculo com dado financeiro: apagar a conta não pode
    -- levar embora a linha do extrato que a menciona.
    CONSTRAINT fk_vault_movements_account FOREIGN KEY (account_id)
        REFERENCES financial_accounts (id) ON DELETE RESTRICT,

    CONSTRAINT fk_vault_movements_user FOREIGN KEY (performed_by_user_id)
        REFERENCES users (id) ON DELETE SET NULL,

    CONSTRAINT uq_vault_movements_public_id UNIQUE (public_id),

    -- **A serialização.** Duas inserções concorrentes disputam o mesmo número e
    -- só uma sobrevive.
    CONSTRAINT uq_vault_movements_sequencia UNIQUE (tenant_id, sequence_number),

    CONSTRAINT ck_vault_movements_direcao CHECK (direction BETWEEN 1 AND 2),
    CONSTRAINT ck_vault_movements_valor CHECK (amount_cents > 0),
    CONSTRAINT ck_vault_movements_sequencia CHECK (sequence_number > 0),

    -- **O cofre não fica negativo.** É esta linha, e não um `if`, que sustenta
    -- a regra.
    CONSTRAINT ck_vault_movements_saldo CHECK (balance_after_cents >= 0)
);

-- O extrato do cofre é sempre "os últimos movimentos desta igreja, em ordem".
-- Descendente porque é assim que a tela lê, e porque é assim que o saldo atual
-- é encontrado: a primeira linha.
CREATE INDEX ix_vault_movements_extrato
    ON vault_movements (tenant_id, sequence_number DESC);

ALTER TABLE vault_movements ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_vault_movements ON vault_movements
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- ---------------------------------------------------------------------------
-- Permissão própria, porque os perfis vêm depois
-- ---------------------------------------------------------------------------
--
-- Administrar o cofre é a operação mais sensível do módulo, e o pedido já
-- anuncia que só alguns perfis terão acesso. Criar a permissão agora significa
-- que restringir depois é **tirar uma linha de `role_permissions`**, e não
-- reescrever o endpoint.
--
-- **Só o Tesoureiro recebe.** O cofre guarda dinheiro em espécie, e essa é
-- literalmente a função da tesouraria. `ChurchAdmin` fica de fora **de
-- propósito**, e não por esquecimento: no seed deste projeto o administrador da
-- igreja tem `giving.read` e não tem `giving.write` — ele enxerga o caixa e não
-- o movimenta. Dar-lhe o cofre entregaria mais poder sobre o dinheiro físico do
-- que ele tem sobre o livro que o registra.
INSERT INTO permissions (code, name)
VALUES ('giving.vault', 'Administrar o cofre da igreja')
ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE r.code = 'Treasurer'
  AND r.tenant_id IS NULL   -- papel de sistema; papel homônimo de um tenant não herda
  AND p.code = 'giving.vault'
ON CONFLICT (role_id, permission_id) DO NOTHING;
