import { describeError } from '@congrega/api-client/errors';
import { listMembers, type Member } from '@congrega/api-client/members';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoAniversariantes {
  readonly membros: readonly Member[];
  readonly total: number;
  readonly carregando: boolean;
  readonly carregandoMais: boolean;
  readonly erro: string | null;
  readonly temMais: boolean;
}

const INICIAL: EstadoAniversariantes = {
  membros: [],
  total: 0,
  carregando: true,
  carregandoMais: false,
  erro: null,
  temMais: false,
};

/**
 * Lista completa de aniversariantes de um mês, com paginação infinita.
 *
 * Mesmo padrão de `useMembers`, mais simples: não há busca para debounce, só
 * o filtro de mês, que não muda depois que a tela abre. `status=Todos` de
 * propósito — quem está de aniversário continua fazendo aniversário mesmo
 * inativo, e esconder isso da secretaria não ajuda ninguém.
 */
export function useAniversariantes(mes: number): EstadoAniversariantes & { carregarMais: () => void } {
  const [estado, setEstado] = useState<EstadoAniversariantes>(INICIAL);
  const pagina = useRef(1);
  const emVoo = useRef<AbortController | null>(null);

  const buscar = useCallback(async (novaPagina: number) => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({
      ...anterior,
      carregando: novaPagina === 1,
      carregandoMais: novaPagina > 1,
      erro: null,
    }));

    try {
      const resultado = await listMembers(
        apiClient,
        { birthdayMonth: mes, status: 'Todos', page: novaPagina, pageSize: 30 },
        controlador.signal,
      );

      setEstado((anterior) => ({
        membros: novaPagina === 1 ? resultado.items : [...anterior.membros, ...resultado.items],
        total: resultado.totalCount,
        carregando: false,
        carregandoMais: false,
        erro: null,
        temMais: resultado.hasNext,
      }));

      pagina.current = novaPagina;
    } catch (causa) {
      if (controlador.signal.aborted) return;

      setEstado((anterior) => ({
        ...anterior,
        carregando: false,
        carregandoMais: false,
        erro: describeError(causa),
      }));
    }
  }, [mes]);

  useEffect(() => {
    void buscar(1);
    return () => emVoo.current?.abort();
  }, [buscar]);

  const carregarMais = useCallback(() => {
    if (estado.temMais && !estado.carregando && !estado.carregandoMais) {
      void buscar(pagina.current + 1);
    }
  }, [buscar, estado.carregando, estado.carregandoMais, estado.temMais]);

  return { ...estado, carregarMais };
}
