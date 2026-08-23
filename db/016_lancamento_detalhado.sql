-- Lançamento financeiro ganha título, tipo próprio, conta, recorrência e cor de
-- categoria.
--
-- ============================================================================
-- A mudança estrutural: o SINAL muda de dono
-- ============================================================================
--
-- O `db/006_financeiro.sql` registrou, com todas as letras:
--
--   "Centavos, sempre positivo. O sinal vem de giving_categories.kind.
--    Permitir valor negativo criaria duas formas de representar uma saída, e um
--    dia as duas apareceriam somadas no mesmo relatório."
--
-- **O que invalida a decisão é a categoria poder ser "ambos".** Uma categoria
-- que serve a entrada E saída — "Outros", "Eventos" — não tem sinal para
-- emprestar, e o fechamento não teria como somá-la. Enquanto todo `kind` era
-- definido, a categoria bastava; com `Ambos` no conjunto, ela deixa de bastar.
--
-- Então o sinal desce para o lançamento, e a categoria vira **restrição**:
--
--   * `giving_entries.kind` é a verdade. O fechamento soma por ele.
--   * `giving_categories.kind` diz o que a categoria ACEITA. `Ambos` aceita os
--     dois; `Entrada` só aceita entrada.
--
-- O valor continua sempre positivo, e o motivo original continua valendo — o
-- que mudou foi qual coluna carrega o sinal, não quantas.

-- ---------------------------------------------------------------------------
-- Categoria: aceita "ambos" e ganha cor
-- ---------------------------------------------------------------------------

ALTER TABLE giving_categories DROP CONSTRAINT ck_giving_categories_kind;
ALTER TABLE giving_categories
    ADD CONSTRAINT ck_giving_categories_kind CHECK (kind BETWEEN 1 AND 3);  -- 3 = Ambos

-- Cor da categoria no resumo por categoria.
--
-- Guardada em hexadecimal e escolhida pela igreja, não derivada do nome: um
-- hash daria cores estáveis mas arbitrárias, e "Dízimo" poderia sair vermelho.
-- Nula significa "use a cor padrão do sistema" — a tela não deve inventar uma.
--
-- **Não carrega significado sozinha.** Verde e âmbar do sistema têm luminância
-- quase idêntica (ver `tokens.test.ts`), e a barra do resumo sempre vem com o
-- nome e o valor escritos ao lado.
ALTER TABLE giving_categories ADD COLUMN color_hex CHAR(7);

ALTER TABLE giving_categories
    ADD CONSTRAINT ck_giving_categories_cor
        CHECK (color_hex IS NULL OR color_hex ~ '^#[0-9A-Fa-f]{6}$');

-- ---------------------------------------------------------------------------
-- Conta financeira — caixa físico ou conta bancária
-- ---------------------------------------------------------------------------
CREATE TABLE financial_accounts (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id   UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id   BIGINT       NOT NULL,

    name        VARCHAR(100) NOT NULL,

    -- 1=Caixa 2=ContaBancaria
    kind        SMALLINT     NOT NULL DEFAULT 1,

    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,

    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_financial_accounts_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_financial_accounts_public_id UNIQUE (public_id),
    CONSTRAINT ck_financial_accounts_kind CHECK (kind BETWEEN 1 AND 2)
);

CREATE UNIQUE INDEX uq_financial_accounts_tenant_nome
    ON financial_accounts (tenant_id, lower(name));

ALTER TABLE financial_accounts ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_financial_accounts ON financial_accounts
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);

-- ---------------------------------------------------------------------------
-- Lançamento: título, tipo, conta, recorrência
-- ---------------------------------------------------------------------------

-- Título do lançamento, separado da categoria.
--
-- Hoje a listagem mostra o NOME DA CATEGORIA como título da linha, porque não
-- há outro campo — e é por isso que a tela repete "Dízimo / Categoria: Dízimo".
-- Com o título próprio, "Aluguel" fica sob a categoria "Contas fixas", que é o
-- que a linha quer dizer.
--
-- Nulo é permitido: os lançamentos que já existem não têm título, e inventar um
-- a partir da categoria produziria exatamente a repetição que este campo veio
-- resolver. A tela cai no nome da categoria quando o título falta.
ALTER TABLE giving_entries ADD COLUMN description VARCHAR(200);

-- 1=Entrada 2=Saida. A partir daqui, é ESTE campo que o fechamento soma.
ALTER TABLE giving_entries ADD COLUMN kind SMALLINT;

-- Preenche a partir da categoria, que hoje é a autoridade. Só depois a coluna
-- vira NOT NULL — na ordem inversa, a constraint recusaria toda linha existente.
UPDATE giving_entries e
SET kind = c.kind
FROM giving_categories c
WHERE c.id = e.category_id;

ALTER TABLE giving_entries ALTER COLUMN kind SET NOT NULL;
ALTER TABLE giving_entries
    ADD CONSTRAINT ck_giving_entries_kind CHECK (kind BETWEEN 1 AND 2);

-- Conta. Opcional: uma igreja com caixa único não deve ser obrigada a cadastrar
-- uma conta para lançar.
--
-- RESTRICT, e não SET NULL: apagar uma conta que tem lançamento apagaria a
-- conciliação de tudo que passou por ela. Quem quer parar de usar desativa.
ALTER TABLE giving_entries ADD COLUMN account_id BIGINT;

ALTER TABLE giving_entries
    ADD CONSTRAINT fk_giving_entries_account FOREIGN KEY (account_id)
        REFERENCES financial_accounts (id) ON DELETE RESTRICT;

CREATE INDEX ix_giving_entries_account ON giving_entries (account_id)
    WHERE account_id IS NOT NULL;

-- Recorrência.
--
-- **Só marca a intenção; não gera nada.** Criar o lançamento do mês seguinte
-- automaticamente exige um worker com janela de execução, idempotência por
-- período e uma regra para o que fazer quando a igreja edita a série — nenhuma
-- dessas coisas existe, e uma delas errada duplica dinheiro no relatório. A
-- coluna registra que o lançamento se repete; a geração é fatia própria.
ALTER TABLE giving_entries ADD COLUMN is_recurring BOOLEAN NOT NULL DEFAULT FALSE;

-- 1=Semanal 2=Mensal 3=Anual
ALTER TABLE giving_entries ADD COLUMN recurrence SMALLINT;

-- A frequência pertence ao recorrente e a mais nada — regra positiva, para não
-- abrir buraco quando um quarto valor entrar. Foi a lição de
-- `db/015_tipos_de_residencia.sql`, onde a versão negativa passaria a permitir
-- andar em sítio.
ALTER TABLE giving_entries
    ADD CONSTRAINT ck_giving_entries_recorrencia CHECK (
        (is_recurring = TRUE AND recurrence BETWEEN 1 AND 3)
     OR (is_recurring = FALSE AND recurrence IS NULL)
    );

-- Formas de pagamento novas: 7=CartaoCredito 8=CartaoDebito 9=Boleto.
--
-- `Cartao` (3) **fica**, e não vira crédito nem débito: os lançamentos já
-- gravados com ele não dizem qual era, e escolher um seria inventar informação
-- financeira. O formulário deixa de oferecê-lo; a listagem continua sabendo
-- desenhá-lo.
ALTER TABLE giving_entries DROP CONSTRAINT ck_giving_entries_method;
ALTER TABLE giving_entries
    ADD CONSTRAINT ck_giving_entries_method CHECK (method BETWEEN 1 AND 9);

-- Teto das observações. O domínio já vai recusar antes, mas a constraint cobre
-- a importação e o script de correção que alguém rodar às pressas.
ALTER TABLE giving_entries
    ADD CONSTRAINT ck_giving_entries_notes CHECK (notes IS NULL OR length(notes) <= 500);

-- Filtro por tipo dentro do mês — o chip "Entradas"/"Saídas" da listagem.
CREATE INDEX ix_giving_entries_tenant_kind
    ON giving_entries (tenant_id, kind, occurred_on DESC);

-- ---------------------------------------------------------------------------
-- Categorias iniciais
-- ---------------------------------------------------------------------------
--
-- Semeadas para cada igreja que já existe. Igreja criada DEPOIS nasce sem
-- categoria — e isso é deliberado, pelo mesmo motivo dos tipos de evento:
-- quando existir onboarding de igreja (hoje `Tenant.Create` não é chamado por
-- nenhum handler), é lá que a semeadura entra, e não num gatilho escondido.
--
-- **A comparação ignora acento**, e o `ON CONFLICT` sozinho não bastaria.
--
-- O índice único é por `lower(name)`, que trata "Dizimo" e "Dízimo" como nomes
-- diferentes — então uma igreja que digitou o dela sem acento ganharia a
-- semeada ao lado, e o resumo por categoria partiria em duas linhas que são a
-- mesma coisa. Aconteceu no banco de desenvolvimento antes desta correção.
--
-- `unaccent` não é imutável e não pode entrar num índice único, então a
-- verificação fica aqui, no `NOT EXISTS`. O `ON CONFLICT` continua como rede
-- para a corrida entre duas execuções simultâneas.
INSERT INTO giving_categories (tenant_id, name, kind, color_hex)
SELECT t.id, padrao.name, padrao.kind, padrao.cor
FROM tenants t
CROSS JOIN (VALUES
    ('Dízimo',       1, '#44831A'),
    ('Oferta',       1, '#6BA83A'),
    ('Contas fixas', 2, '#B3453F'),
    ('Manutenção',   2, '#A45C00'),
    ('Eventos',      3, '#355A6D'),
    ('Outros',       3, '#68716B')
) AS padrao(name, kind, cor)
WHERE NOT EXISTS (
    SELECT 1
    FROM giving_categories existente
    WHERE existente.tenant_id = t.id
      AND lower(congrega_unaccent(existente.name)) = lower(congrega_unaccent(padrao.name))
)
ON CONFLICT DO NOTHING;
