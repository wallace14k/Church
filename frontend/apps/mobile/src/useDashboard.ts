import { listUpcomingEvents, type CalendarEvent } from '@congrega/api-client/events';
import { listMembers, type Member } from '@congrega/api-client/members';
import { describeFailure, type Failure } from '@congrega/api-client/errors';
import { useEffect, useState } from 'react';
import { apiClient } from './api';

interface DadosDashboard {
  readonly carregando: boolean;
  readonly erro: Failure | null;
  readonly totalMembros: number | null;
  readonly aniversariantes: readonly Member[];

  /**
   * Eventos que começam nos próximos 7 dias.
   *
   * A janela é aplicada aqui e não na API: `/events/upcoming` recebe um
   * limite de quantidade, não de período. Pedir 20 e cortar por data no
   * cliente responde "quantos eventos há nesta semana" sem endpoint novo — e
   * 20 é folga suficiente para uma semana de igreja.
   */
  readonly proximosEventos: readonly CalendarEvent[];

}

export interface EstadoDashboard extends DadosDashboard {
  /** Refaz as três consultas. É o que o botão "tentar de novo" chama. */
  readonly recarregar: () => void;
}

const INICIAL: DadosDashboard = {
  carregando: true,
  erro: null,
  totalMembros: null,
  aniversariantes: [],
  proximosEventos: [],
};

/** Janela do cartão "eventos próximos". */
const JANELA_EM_DIAS = 7;

/**
 * Quantos eventos pedir para cobrir a janela.
 *
 * Folga deliberada: a API limita por quantidade e o corte por data é feito
 * aqui, então pedir de menos esconderia eventos da semana numa igreja com
 * agenda cheia. Pedir 20 custa uma resposta pequena e nunca mente.
 */
const LIMITE_DE_EVENTOS = 20;

/**
 * Dados reais para o painel de início: quantos membros a igreja tem, e quem
 * faz aniversário este mês.
 *
 * Duas chamadas em paralelo à mesma listagem de membros — uma pedindo só a
 * contagem (`pageSize: 1`), outra filtrada por `birthdayMonth`. Não existe
 * endpoint de estatísticas dedicado, e criar um para dois números seria
 * infraestrutura sem uso real ainda; a listagem já resolve os dois.
 */
export function useDashboard(temIgreja: boolean): EstadoDashboard {
  // Recarrega sob demanda: o `AsyncContent` oferece "tentar de novo" quando a
  // falha permite, e sem isto o botão não teria o que chamar.
  const [versao, setVersao] = useState(0);

  const [estado, setEstado] = useState<DadosDashboard>(temIgreja ? INICIAL : { ...INICIAL, carregando: false });

  useEffect(() => {
    if (!temIgreja) return;

    let cancelado = false;
    const controlador = new AbortController();

    async function carregar() {
      try {
        const mesAtual = new Date().getMonth() + 1;

        const [contagem, aniversariantesDoMes, eventos] = await Promise.all([
          listMembers(apiClient, { pageSize: 1 }, controlador.signal),
          listMembers(apiClient, { birthdayMonth: mesAtual, pageSize: 8 }, controlador.signal),
          listUpcomingEvents(apiClient, LIMITE_DE_EVENTOS, controlador.signal),
        ]);

        if (cancelado) return;

        const fimDaJanela = Date.now() + JANELA_EM_DIAS * 24 * 60 * 60 * 1000;

        setEstado({
          carregando: false,
          erro: null,
          totalMembros: contagem.totalCount,
          aniversariantes: aniversariantesDoMes.items,
          proximosEventos: eventos.filter((e) => new Date(e.startsAt).getTime() <= fimDaJanela),
        });
      } catch (causa) {
        if (cancelado || controlador.signal.aborted) return;
        setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeFailure(causa) }));
      }
    }

    void carregar();
    return () => {
      cancelado = true;
      controlador.abort();
    };
  }, [temIgreja, versao]);

  return { ...estado, recarregar: () => setVersao((v) => v + 1) };
}
