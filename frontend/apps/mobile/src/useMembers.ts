import { describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  getMemberSummary,
  listMembers,
  type Member,
  type MemberGap,
  type MemberSummary,
} from '@congrega/api-client/members';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoMembros {
  readonly membros: readonly Member[];
  readonly total: number;
  readonly carregando: boolean;
  readonly carregandoMais: boolean;
  readonly erro: Failure | null;
  readonly temMais: boolean;

  /**
   * Contagens do acervo, para os chips de filtro.
   *
   * `null` enquanto carrega ou se a consulta falhar — e os chips então saem da
   * tela em vez de mostrar zero. Um chip "Sem telefone 0" numa igreja que tem
   * três seria pior do que chip nenhum.
   */
  readonly resumo: MemberSummary | null;
}

const INICIAL: EstadoMembros = {
  membros: [],
  total: 0,
  carregando: true,
  carregandoMais: false,
  erro: null,
  temMais: false,
  resumo: null,
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
export function useMembers(
  busca: string,
  filtro?: MemberGap,
): EstadoMembros & { carregarMais: () => void; recarregar: () => void } {
  const [estado, setEstado] = useState<EstadoMembros>(INICIAL);

  // Cancela a requisição anterior quando a busca muda. Sem isso, a resposta de
  // "jo" pode chegar depois da de "joão" e sobrescrever a lista com o resultado
  // errado — a corrida clássica de busca conforme se digita.
  const emVoo = useRef<AbortController | null>(null);
  const pagina = useRef(1);

  // Refazer o resumo é pedido explícito, não consequência de digitar.
  const [versaoDoResumo, setVersaoDoResumo] = useState(0);

  const buscar = useCallback(async (termo: string, novaPagina: number, lacuna: MemberGap | undefined) => {
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
        {
          search: termo,
          page: novaPagina,
          pageSize: 30,
          ...(lacuna === undefined ? {} : { gap: lacuna }),
        },
        controlador.signal,
      );

      setEstado((anterior) => ({
        ...anterior,
        // Página 1 substitui; as seguintes acumulam.
        membros: novaPagina === 1 ? resultado.items : [...anterior.membros, ...resultado.items],
        total: resultado.totalCount,
        carregando: false,
        carregandoMais: false,
        erro: null,
        temMais: resultado.hasNext,
        // O `...anterior` acima preserva `resumo`. Montar o objeto do zero
        // apagaria as contagens a cada tecla digitada, e os chips piscariam
        // para fora da tela durante a busca.
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
        erro: describeFailure(causa),
      }));
    }
  }, []);

  useEffect(() => {
    const id = setTimeout(() => void buscar(busca, 1, filtro), busca === '' ? 0 : DEBOUNCE_MS);
    return () => clearTimeout(id);
  }, [busca, filtro, buscar]);

  /**
   * O resumo é carregado à parte da listagem, e de propósito.
   *
   * Ele não depende da busca nem do filtro — as contagens são do acervo, e
   * refazê-las a cada tecla digitada seria uma agregação por caractere. Só o
   * `status` as afeta, e a tela ainda não o troca.
   */
  useEffect(() => {
    const controlador = new AbortController();

    getMemberSummary(apiClient, undefined, controlador.signal)
      .then((resumo) => setEstado((anterior) => ({ ...anterior, resumo })))
      .catch(() => {
        // Silencioso: os chips somem, a listagem continua. Barrar a tela porque
        // uma contagem decorativa falhou seria desproporcional.
      });

    return () => controlador.abort();
  }, [versaoDoResumo]);

  // Aborta ao desmontar: sem isso, a resposta chega para uma tela que já saiu e
  // o React avisa sobre atualização de estado em componente desmontado.
  useEffect(() => () => emVoo.current?.abort(), []);

  const carregarMais = useCallback(() => {
    if (estado.temMais && !estado.carregando && !estado.carregandoMais) {
      void buscar(busca, pagina.current + 1, filtro);
    }
  }, [busca, filtro, buscar, estado.carregando, estado.carregandoMais, estado.temMais]);

  // Refaz a busca corrente do zero.
  //
  // A tela tinha um "Tentar de novo" que chamava `setBusca((b) => b)` — e o
  // React descarta atualização de estado idêntico por `Object.is`, então o
  // efeito nunca reexecutava e **o botão não fazia absolutamente nada**.
  // Parecia funcionar, o que é pior do que não existir: quem caía num erro de
  // rede clicava, nada acontecia, e a conclusão razoável era que o app estava
  // quebrado.
  const recarregar = useCallback(() => {
    void buscar(busca, 1, filtro);
    setVersaoDoResumo((v) => v + 1);
  }, [busca, filtro, buscar]);

  return { ...estado, carregarMais, recarregar };
}
