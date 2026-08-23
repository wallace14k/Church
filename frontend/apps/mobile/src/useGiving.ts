import { describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  getMonthlyClosing,
  listGivingCategories,
  listGivingEntries,
  type GivingCategory,
  type GivingEntry,
  type MonthlyClosing,
  getGivingSummary,
  type GivingSummary,
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

export function useGivingEntries(
  year: number,
  month: number,
  filtro?: { readonly kind?: 'Entrada' | 'Saida'; readonly categoryId?: string },
): EstadoLancamentos & {
  readonly resumo: GivingSummary | null;
  readonly recarregar: () => void;
} {
  const [estado, setEstado] = useState<EstadoLancamentos>({
    lancamentos: [],
    total: 0,
    carregando: true,
    erro: null,
  });

  /**
   * Contagens do mês, para os chips.
   *
   * `null` enquanto carrega ou se a consulta falhar — e os chips então saem da
   * tela em vez de mostrar zero. Um chip "Saídas 0" num mês que tem três seria
   * pior do que chip nenhum.
   */
  const [resumo, setResumo] = useState<GivingSummary | null>(null);
  const emVoo = useRef<AbortController | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const resultado = await listGivingEntries(
        apiClient,
        {
          year,
          month,
          pageSize: 100,
          ...(filtro?.kind === undefined ? {} : { kind: filtro.kind }),
          ...(filtro?.categoryId === undefined ? {} : { categoryId: filtro.categoryId }),
        },
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
  }, [year, month, filtro?.kind, filtro?.categoryId]);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  /**
   * O resumo é carregado à parte da listagem, e de propósito.
   *
   * Ele não depende do filtro — as contagens são do mês inteiro, e refazê-las a
   * cada chip clicado devolveria sempre os mesmos números por uma consulta a
   * mais. Só o período as afeta.
   */
  useEffect(() => {
    const controlador = new AbortController();

    getGivingSummary(apiClient, year, month, controlador.signal)
      .then(setResumo)
      .catch(() => {
        // Silencioso: os chips somem, a listagem continua. Barrar o caixa da
        // igreja porque uma contagem acessória falhou seria desproporcional.
      });

    return () => controlador.abort();
  }, [year, month]);

  return { ...estado, resumo, recarregar: carregar };
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
