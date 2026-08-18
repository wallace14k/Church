import {
  getSubscriptionStatus,
  listPlans,
  startCheckout,
  type CheckoutResult,
  type Plan,
  type SubscriptionStatus,
} from '@congrega/api-client/billing';
import { describeError } from '@congrega/api-client/errors';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoBilling {
  readonly assinatura: SubscriptionStatus | null;
  readonly planos: readonly Plan[];
  readonly carregando: boolean;
  readonly erro: string | null;
}

/**
 * Estado da assinatura Congrega+ do usuário e o catálogo de planos — as duas
 * leituras que a aba de assinatura precisa, buscadas juntas porque a tela
 * decide o que mostrar (status ou vitrine de planos) a partir das duas ao
 * mesmo tempo.
 */
export function useBilling(): EstadoBilling & {
  readonly recarregar: () => void;
  readonly assinar: (planCode: string) => Promise<CheckoutResult>;
} {
  const [estado, setEstado] = useState<EstadoBilling>({
    assinatura: null,
    planos: [],
    carregando: true,
    erro: null,
  });
  const emVoo = useRef<AbortController | null>(null);

  // Uma chave por PLANO escolhido, não uma por tela. Repetir a tentativa do
  // mesmo plano (ex.: depois de um erro de rede) reaproveita a chave — é
  // literalmente o caso que `Idempotency-Key` existe para cobrir. Escolher
  // outro plano é uma intenção nova, e ganha uma chave nova.
  const tentativa = useRef<{ planCode: string; key: string } | null>(null);

  const carregar = useCallback(async () => {
    emVoo.current?.abort();
    const controlador = new AbortController();
    emVoo.current = controlador;

    setEstado((anterior) => ({ ...anterior, carregando: true, erro: null }));

    try {
      const [assinatura, planos] = await Promise.all([
        getSubscriptionStatus(apiClient, controlador.signal),
        listPlans(apiClient, controlador.signal),
      ]);
      setEstado({ assinatura, planos, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeError(causa) }));
    }
  }, []);

  useEffect(() => {
    void carregar();
    return () => emVoo.current?.abort();
  }, [carregar]);

  const assinar = useCallback(async (planCode: string) => {
    if (tentativa.current?.planCode !== planCode) {
      tentativa.current = { planCode, key: crypto.randomUUID() };
    }

    return startCheckout(apiClient, planCode, tentativa.current.key);
  }, []);

  return { ...estado, recarregar: carregar, assinar };
}
