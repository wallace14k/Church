-- Cor do tipo de evento.
--
-- A agenda passa a distinguir os tipos por uma faixa colorida na linha e por um
-- ponto no filtro. A cor é **da igreja**, e não derivada do nome: "Vigília" é
-- azul aqui e pode ser roxo na igreja vizinha, e é a igreja que sabe qual
-- convenção os membros dela já reconhecem.
--
-- Mesma coluna, mesmo CHECK e mesmo tipo de `giving_categories.color_hex` — não
-- por simetria, mas porque é o mesmo problema resolvido do mesmo jeito, e duas
-- formas diferentes de guardar uma cor no mesmo banco só criam a dúvida de qual
-- delas é a certa.
--
-- **A cor nunca carrega significado sozinha.** Na tela, ela sempre aparece ao
-- lado do nome do tipo escrito. Uma faixa colorida sem rótulo não diria nada a
-- quem não distingue matiz — e o tipo do evento é justamente o que a faixa
-- existe para comunicar.

ALTER TABLE event_types ADD COLUMN color_hex CHAR(7);

ALTER TABLE event_types
    ADD CONSTRAINT ck_event_types_cor
        CHECK (color_hex IS NULL OR color_hex ~ '^#[0-9A-Fa-f]{6}$');

-- ---------------------------------------------------------------------------
-- Uma cor inicial para o que já existe
-- ---------------------------------------------------------------------------
--
-- Sem isto, toda igreja com tipos já cadastrados abriria a agenda nova com
-- todas as faixas cinzas — e concluiria que a cor não funciona, em vez de que
-- ela ainda não foi escolhida.
--
-- A distribuição é por `id % 6` sobre uma paleta fixa: determinística, estável
-- entre execuções, e diferente entre tipos vizinhos. Não é uma escolha de
-- design — é um ponto de partida que a igreja troca na tela de tipos.
--
-- As seis cores saem da paleta do sistema e foram medidas contra o branco do
-- cartão: todas acima de 3:1, que é o mínimo de 1.4.11 para elemento não
-- textual. A faixa precisa ser vista, não só existir.
UPDATE event_types
SET color_hex = (ARRAY[
    '#44831A',  -- verde do sistema
    '#4A5FBF',  -- azul
    '#7C5CBF',  -- roxo
    '#A45C00',  -- âmbar do sistema
    '#237268',  -- verde-azulado
    '#B3453F'   -- vermelho do sistema
])[(id % 6) + 1]
WHERE color_hex IS NULL;
