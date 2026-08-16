import { describeError } from '@congrega/api-client/errors';
import { listMembers, type Member } from '@congrega/api-client/members';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoMembros {
  readonly membros: readonly Member[];
  readonly total: number;
  readonly carregando: boolean;
  readonly carregandoMais: boolean;
  readonly erro: string | null;
  readonly temMais: boolean;
}

const INICIAL: EstadoMembros = {
  membros: [],
  total: 0,
  carregando: true,
  carregandoMais: false,
  erro: null,
  temMais: false,
};

/** Espera antes de buscar enquanto o usuário digita. */
const DEBOUNCE_MS = 350;

/**
 * Carrega a lista de membros com busca e paginação.
 *
 * Hook próprio em vez de TanStack Query: a tela tem uma única fonte de dados, e
 * trazer uma biblioteca de cache para isso adicionaria peso ao bundle sem
 * resolver problema que já exista. Quando houver várias telas compartilhando
 * dados e precisando de invalidação cruzada, aí a troca se paga.
 */
export function useMembers(busca: string): EstadoMembros & { carregarMais: () => void } {
  const [estado, setEstado] = useState<EstadoMembros>(INICIAL);

  // Cancela a requisição anterior quando a busca muda. Sem isso, a resposta de
  // "jo" pode chegar depois da de "joão" e sobrescrever a lista com o resultado
  // errado — a corrida clássica de busca conforme se digita.
  const emVoo = useRef<AbortController | null>(null);
  const pagina = useRef(1);

  const buscar = useCallback(async (termo: string, novaPagina: number) => {
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
        { search: termo, page: novaPagina, pageSize: 30 },
        controlador.signal,
      );

      setEstado((anterior) => ({
        // Página 1 substitui; as seguintes acumulam.
        membros: novaPagina === 1 ? resultado.items : [...anterior.membros, ...resultado.items],
        total: resultado.totalCount,
        carregando: false,
        carregandoMais: false,
        erro: null,
        temMais: resultado.hasNext,
      }));

      pagina.current = novaPagina;
    } catch (causa) {
      // Cancelamento não é erro: é a busca anterior sendo descartada de
      // propósito. Mostrá-lo faria a tela piscar mensagem de falha a cada tecla.
      if (controlador.signal.aborted) return;

      setEstado((anterior) => ({
        ...anterior,
        carregando: false,
        carregandoMais: false,
        erro: describeError(causa),
      }));
    }
  }, []);

  useEffect(() => {
    const id = setTimeout(() => void buscar(busca, 1), busca === '' ? 0 : DEBOUNCE_MS);
    return () => clearTimeout(id);
  }, [busca, buscar]);

  // Aborta ao desmontar: sem isso, a resposta chega para uma tela que já saiu e
  // o React avisa sobre atualização de estado em componente desmontado.
  useEffect(() => () => emVoo.current?.abort(), []);

  const carregarMais = useCallback(() => {
    if (estado.temMais && !estado.carregando && !estado.carregandoMais) {
      void buscar(busca, pagina.current + 1);
    }
  }, [busca, buscar, estado.carregando, estado.carregandoMais, estado.temMais]);

  return { ...estado, carregarMais };
}
