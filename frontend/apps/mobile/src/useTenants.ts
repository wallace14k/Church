import { listAvailableTenants, switchTenant, type TenantSummary } from '@congrega/api-client/auth';
import { useEffect, useState } from 'react';
import { apiClient } from './api';
import { useSession } from './session';

export interface EstadoTenants {
  readonly tenants: readonly TenantSummary[];
  readonly atual: TenantSummary | null;
  readonly trocando: boolean;
  readonly trocar: (tenantId: string) => Promise<void>;
}

/**
 * Igrejas do usuário e a troca entre elas.
 *
 * Compartilhado entre a sidebar (seletor no topo) e o painel de início (nome
 * da igreja no cabeçalho) — os dois precisam exatamente da mesma lista, e
 * buscá-la duas vezes só duplicaria a chamada sem ganho nenhum.
 */
export function useTenants(): EstadoTenants {
  const { session, status } = useSession();
  const [tenants, setTenants] = useState<readonly TenantSummary[]>([]);
  const [trocando, setTrocando] = useState(false);

  useEffect(() => {
    if (status !== 'autenticado') return;
    let cancelado = false;

    listAvailableTenants(apiClient)
      .then((lista) => {
        if (!cancelado) setTenants(lista);
      })
      .catch(() => {
        // Sem a lista, o seletor de igreja não aparece — degrada para o nome
        // genérico, não quebra a tela.
      });

    return () => {
      cancelado = true;
    };
  }, [status, session?.tenantId]);

  const atual = tenants.find((t) => t.id === session?.tenantId) ?? null;

  async function trocar(tenantId: string) {
    if (tenantId === session?.tenantId) return;
    setTrocando(true);
    try {
      await switchTenant(apiClient, tenantId);
    } finally {
      setTrocando(false);
    }
  }

  return { tenants, atual, trocando, trocar };
}
