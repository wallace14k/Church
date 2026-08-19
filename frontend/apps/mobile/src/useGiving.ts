import { describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  getMonthlyClosing,
  listGivingCategories,
  listGivingEntries,
  type GivingCategory,
  type GivingEntry,
  type MonthlyClosing,
} from '@congrega/api-client/giving';
import { monthName, shiftMonth, type YearMonth } from '@congrega/core/datetime';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

/** Mês corrente. */
export function mesCorrente(): YearMonth {
  const agora = new Date();
  return { year: agora.getFullYear(), month: agora.getMonth() + 1 };
}

// A aritmética de mês mora em `@congrega/core/datetime`, onde tem teste — a
// virada de ano é fácil de errar e impossível de notar olhando a tela em agosto.
export { monthName as nomeDoMes, shiftMonth as deslocarMes };
export type { YearMonth };

interface EstadoCategorias {
  readonly categorias: readonly GivingCategory[];
  readonly carregando: boolean;
  readonly erro: Failure | null;
}

export function useGivingCategories(includeInactive = false): EstadoCategorias & {
  readonly recarregar: () => void;
} {
  const [estado, setEstado] = useState<EstadoCategorias>({
    categorias: [],
    carregando: true,
    erro: null,
  });
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const categorias = await listGivingCategories(apiClient, includeInactive, controlador.signal);
      setEstado({ categorias, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeFailure(causa) }));
    }
  }, [includeInactive]);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  return { ...estado, recarregar: carregar };
}

interface EstadoLancamentos {
  readonly lancamentos: readonly GivingEntry[];
  readonly total: number;
  readonly carregando: boolean;
  readonly erro: Failure | null;
}

export function useGivingEntries(year: number, month: number): EstadoLancamentos & {
  readonly recarregar: () => void;
} {
  const [estado, setEstado] = useState<EstadoLancamentos>({
    lancamentos: [],
    total: 0,
    carregando: true,
    erro: null,
  });
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const resultado = await listGivingEntries(
        apiClient,
        { year, month, pageSize: 100 },
        controlador.signal,
      );
      setEstado({
        lancamentos: resultado.items,
        total: resultado.totalCount,
        carregando: false,
        erro: null,
      });
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

interface EstadoFechamento {
  readonly fechamento: MonthlyClosing | null;
  readonly carregando: boolean;
  readonly erro: Failure | null;
}

export function useMonthlyClosing(year: number, month: number): EstadoFechamento & {
  readonly recarregar: () => void;
} {
  const [estado, setEstado] = useState<EstadoFechamento>({
    fechamento: null,
    carregando: true,
    erro: null,
  });
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const fechamento = await getMonthlyClosing(apiClient, year, month, controlador.signal);
      setEstado({ fechamento, carregando: false, erro: null });
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
