-- Lançamentos periódicos: a série de 12 meses, e o estado que a torna segura.
--
-- ============================================================================
-- O problema que a geração cria, e que esta migration resolve antes
-- ============================================================================
--
-- `GivingEntry.Register` recusa data futura, com motivo registrado:
--
--   "Data futura em livro-caixa é erro de digitação — quase sempre o ano. Sem a
--    barreira, um lançamento de 2027 sairia silenciosamente do fechamento do
--    mês e ninguém acharia o dinheiro que 'sumiu'."
--
-- Gerar os próximos 12 meses significa criar 12 linhas com data futura. Sem
-- mais nada, o caixa de setembro passaria a afirmar que o aluguel de setembro
-- **já foi pago** — dinheiro que não se moveu aparecendo como movimentado. Num
-- livro-caixa isso não é um detalhe de interface: é a prestação de contas
-- errada.
--
-- A solução é a que a contabilidade usa há séculos: separar **previsto** de
-- **realizado**. As 12 linhas existem, aparecem na agenda financeira e podem
-- ser editadas — e **não entram no fechamento** até serem confirmadas.

-- 1=Realizado 2=Previsto
--
-- O padrão é Realizado: todo lançamento que existia até aqui é dinheiro que já
-- se moveu, e a coluna nasce refletindo isso sem precisar de UPDATE.
ALTER TABLE giving_entries ADD COLUMN status SMALLINT NOT NULL DEFAULT 1;

ALTER TABLE giving_entries
    ADD CONSTRAINT ck_giving_entries_status CHECK (status BETWEEN 1 AND 2);

-- Identidade da série.
--
-- Sem ela, editar "o aluguel" significaria editar doze linhas soltas que só a
-- memória de quem criou liga entre si. Com ela, cancelar a série é uma
-- operação, e não doze exclusões que alguém pode deixar pela metade.
--
-- UUID gerado pela aplicação, e não `bigserial`: a série precisa ser nomeável
-- na URL da tela que a administra, e chave sequencial exposta é enumeração.
ALTER TABLE giving_entries ADD COLUMN series_id UUID;

-- **Esta é a proteção contra duplicar dinheiro.**
--
-- Se a geração rodar duas vezes — clique duplo, retry de rede, worker
-- reprocessando — a segunda tentativa é recusada pela constraint em vez de
-- gravar uma segunda parcela do mesmo mês. É a mesma escolha do resto do
-- projeto: correção vem de constraint, não de `if (!existe)`, que sob
-- concorrência tem janela.
CREATE UNIQUE INDEX uq_giving_entries_serie_data
    ON giving_entries (series_id, occurred_on)
    WHERE series_id IS NOT NULL;

-- A agenda financeira ("o que vem pela frente") e o fechamento ("o que já
-- aconteceu") filtram por status dentro do tenant.
CREATE INDEX ix_giving_entries_tenant_status
    ON giving_entries (tenant_id, status, occurred_on);

CREATE INDEX ix_giving_entries_serie ON giving_entries (series_id)
    WHERE series_id IS NOT NULL;

-- ---------------------------------------------------------------------------
-- Nota sobre o que NÃO está aqui
-- ---------------------------------------------------------------------------
--
-- Não há CHECK proibindo data futura em lançamento realizado. A regra é do
-- domínio, e precisa ser: um CHECK contra `now()` tornaria a linha inválida
-- retroativamente — o lançamento de hoje continua válido amanhã, mas um
-- `CHECK (occurred_on <= CURRENT_DATE)` não é imutável e o Postgres recusa a
-- constraint. Fica em `GivingEntry.Register`, onde a data é comparada com o
-- relógio injetado.
