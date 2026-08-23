-- Eventos recorrentes semanais: "todo domingo tem culto às 19h".
--
-- ============================================================================
-- Por que linhas de verdade, e não uma regra de repetição
-- ============================================================================
--
-- A alternativa seria guardar UMA linha com "repete toda semana" e expandir a
-- série na leitura. É o que fazem os calendários grandes, e é o desenho errado
-- aqui — por uma razão concreta: a igreja precisa **mexer numa ocorrência**.
-- O culto do dia 25 vai ser no salão, não no templo; o de 1º de novembro está
-- cancelado por causa do feriado. Com a regra expandida na leitura, cada exceção
-- vira uma tabela de exceções e a consulta da agenda passa a ser regra menos
-- exceções mais eventos avulsos.
--
-- Com linhas de verdade, uma exceção é editar uma linha. A agenda continua sendo
-- um SELECT, e o que existe no banco é exatamente o que a igreja vê.
--
-- O custo é o horizonte: 52 linhas por série anual. Para uma igreja com dez
-- eventos semanais são 520 linhas por ano — nada, para um banco.

ALTER TABLE events ADD COLUMN series_id UUID;

-- **Esta é a proteção contra duplicar a agenda.**
--
-- Se a geração rodar duas vezes — clique duplo, retry de rede — a segunda
-- tentativa é recusada pela constraint em vez de gravar um segundo culto no
-- mesmo domingo, no mesmo horário. É a mesma escolha do resto do projeto:
-- correção vem de constraint, não de `if (!existe)`, que sob concorrência tem
-- janela.
--
-- Parcial (`WHERE series_id IS NOT NULL`) porque eventos avulsos não têm série,
-- e sem o filtro todos eles colidiriam entre si em `(NULL, starts_at)` — não,
-- NULL não colide em índice único, mas o índice parcial ainda é o certo: ele
-- não gasta espaço com as linhas que a regra não governa.
CREATE UNIQUE INDEX uq_events_serie_inicio
    ON events (series_id, starts_at)
    WHERE series_id IS NOT NULL;

-- Apagar a série inteira é "todas as linhas com este series_id" — a consulta que
-- torna viável desfazer um "todo domingo" sem cinquenta e duas exclusões.
CREATE INDEX ix_events_serie ON events (series_id)
    WHERE series_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- O que NÃO está aqui
-- ---------------------------------------------------------------------------
--
-- Não há tabela de definição de série — nada guarda "esta série repete toda
-- semana às 19h". O `series_id` é só identidade: liga as linhas para que
-- apagá-las juntas seja possível.
--
-- A consequência é deliberada: **não existe "editar a série"**. Mudar o horário
-- de todos os cultos futuros exige apagar a série e criar outra. Guardar a
-- definição permitiria a edição em massa, e traria junto a pergunta que todo
-- calendário erra — "esta ocorrência, as futuras, ou todas?" — cuja resposta
-- errada reescreve o passado da igreja.
