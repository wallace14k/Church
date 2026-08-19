import { describeFailure, type Failure } from '@congrega/api-client/errors';
import { listEvents, listUpcomingEvents, type CalendarEvent } from '@congrega/api-client/events';
import { businessMonthRange, type YearMonth } from '@congrega/core/datetime';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoAgenda {
  readonly eventos: readonly CalendarEvent[];
  readonly carregando: boolean;
  readonly erro: Failure | null;
}

const INICIAL: EstadoAgenda = { eventos: [], carregando: true, erro: null };

/** Eventos que tocam o mês — inclusive os que começaram antes e ainda não terminaram. */
export function useEventsOfMonth(period: YearMonth): EstadoAgenda & { readonly recarregar: () => void } {
  const [estado, setEstado] = useState<EstadoAgenda>(INICIAL);
  const emVoo = useRef<AbortController | null>(null);

  const { year, month } = period;

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      // Janela calculada no fuso de negócio, não no do aparelho: ver
      // `businessMonthRange` em @congrega/core/datetime.
      const janela = businessMonthRange({ year, month });
      const eventos = await listEvents(apiClient, janela, controlador.signal);
      setEstado({ eventos, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeFailure(causa) }));
    }
  }, [year, month]);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  return { ...estado, recarregar: carregar };
}

/** Próximos compromissos, para o painel de início. */
export function useUpcomingEvents(limit = 3): EstadoAgenda & { readonly recarregar: () => void } {
  const [estado, setEstado] = useState<EstadoAgenda>(INICIAL);
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const eventos = await listUpcomingEvents(apiClient, limit, controlador.signal);
      setEstado({ eventos, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeFailure(causa) }));
    }
  }, [limit]);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  return { ...estado, recarregar: carregar };
}
