import { listMembers, type Member } from '@congrega/api-client/members';
import { describeError } from '@congrega/api-client/errors';
import { useEffect, useState } from 'react';
import { apiClient } from './api';

export interface EstadoDashboard {
  readonly carregando: boolean;
  readonly erro: string | null;
  readonly totalMembros: number | null;
  readonly aniversariantes: readonly Member[];
}

const INICIAL: EstadoDashboard = {
  carregando: true,
  erro: null,
  totalMembros: null,
  aniversariantes: [],
};

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
  const [estado, setEstado] = useState<EstadoDashboard>(temIgreja ? INICIAL : { ...INICIAL, carregando: false });

  useEffect(() => {
    if (!temIgreja) return;

    let cancelado = false;
    const controlador = new AbortController();

    async function carregar() {
      try {
        const mesAtual = new Date().getMonth() + 1;

        const [contagem, aniversariantesDoMes] = await Promise.all([
          listMembers(apiClient, { pageSize: 1 }, controlador.signal),
          listMembers(apiClient, { birthdayMonth: mesAtual, pageSize: 8 }, controlador.signal),
        ]);

        if (cancelado) return;

        setEstado({
          carregando: false,
          erro: null,
          totalMembros: contagem.totalCount,
          aniversariantes: aniversariantesDoMes.items,
        });
      } catch (causa) {
        if (cancelado || controlador.signal.aborted) return;
        setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeError(causa) }));
      }
    }

    void carregar();
    return () => {
      cancelado = true;
      controlador.abort();
    };
  }, [temIgreja]);

  return estado;
}
