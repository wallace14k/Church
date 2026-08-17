-- =============================================================================
-- Congrega — Onda 2: calendário de eventos
-- =============================================================================
--
-- **Sem recorrência, de propósito.** O doc 05 inclui o calendário no MVP com a
-- justificativa "barato de construir", e recorrência de verdade não é: exige a
-- regra (RRULE), a lista de exceções, uma estratégia de materialização para
-- consultar intervalo, e tratamento de horário de verão — "todo domingo às 19h"
-- muda de instante UTC quando o fuso muda, e resolver isso errado faz o culto
-- aparecer às 18h para a igreja inteira. Uma tabela `event_series` entra quando
-- houver decisão explícita sobre esses quatro pontos.
--
-- O que o check-in infantil (Onda 4) precisa é de uma ocorrência concreta para
-- ancorar a criança — e isso esta tabela já entrega.

CREATE TABLE events (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id   UUID         NOT NULL DEFAULT gen_random_uuid(),
    tenant_id   BIGINT       NOT NULL,

    title       VARCHAR(200) NOT NULL,
    description TEXT,
    location    VARCHAR(200),

    -- TIMESTAMPTZ e UTC na persistência; a borda converte para
    -- America/Sao_Paulo. Guardar horário local sem fuso faria o mesmo culto
    -- aparecer em horas diferentes para quem abrisse o app viajando.
    starts_at   TIMESTAMPTZ  NOT NULL,
    ends_at     TIMESTAMPTZ  NOT NULL,

    -- 1=Agendado 2=Cancelado. Evento cancelado CONTINUA visível: apagá-lo faria
    -- quem já sabia do culto aparecer na porta da igreja fechada. O cancelamento
    -- é a informação, não a ausência dela.
    status      SMALLINT     NOT NULL DEFAULT 1,

    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_events_tenant FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT uq_events_public_id UNIQUE (public_id),
    CONSTRAINT ck_events_status CHECK (status BETWEEN 1 AND 2),

    -- Fim antes do começo é erro de digitação, e sem a barreira o evento
    -- desaparece de qualquer consulta por intervalo — some sem avisar.
    CONSTRAINT ck_events_periodo CHECK (ends_at > starts_at)
);

-- A consulta do calendário é sempre "o que acontece entre duas datas nesta
-- igreja". Sem este índice, cada abertura da agenda varre a tabela inteira.
CREATE INDEX ix_events_tenant_inicio ON events (tenant_id, starts_at);

ALTER TABLE events ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_events ON events
    FOR ALL USING (tenant_id = NULLIF(current_setting('app.tenant_id', TRUE), '')::BIGINT);
