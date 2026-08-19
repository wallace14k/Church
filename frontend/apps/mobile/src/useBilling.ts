import {
  cancelSubscription,
  getSubscriptionStatus,
  listPayments,
  listPlans,
  startCheckout,
  type CheckoutResult,
  type Payment,
  type Plan,
  type SubscriptionStatus,
} from '@congrega/api-client/billing';
import { describeFailure, type Failure } from '@congrega/api-client/errors';
import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from './api';

interface EstadoBilling {
  readonly assinatura: SubscriptionStatus | null;
  readonly planos: readonly Plan[];
  readonly pagamentos: readonly Payment[];
  readonly carregando: boolean;
  readonly erro: Failure | null;
}

/**
 * Estado da assinatura Congrega+ do usuário, catálogo de planos e histórico de
 * cobranças — as três leituras que a aba de assinatura precisa, buscadas juntas
 * porque a tela decide o que mostrar (status ou vitrine) a partir da primeira e
 * renderiza as outras duas na mesma passagem.
 */
export function useBilling(): EstadoBilling & {
  readonly recarregar: () => void;
  readonly assinar: (planCode: string) => Promise<CheckoutResult>;
  readonly cancelar: () => Promise<void>;
} {
  const [estado, setEstado] = useState<EstadoBilling>({
    assinatura: null,
    planos: [],
    pagamentos: [],
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
      const [assinatura, planos, pagamentos] = await Promise.all([
        getSubscriptionStatus(apiClient, controlador.signal),
        listPlans(apiClient, controlador.signal),
        listPayments(apiClient, controlador.signal),
      ]);
      setEstado({ assinatura, planos, pagamentos, carregando: false, erro: null });
    } catch (causa) {
      if (controlador.signal.aborted) return;
      setEstado((anterior) => ({ ...anterior, carregando: false, erro: describeFailure(causa) }));
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

  const cancelar = useCallback(async () => {
    // A resposta do cancelamento já vem no mesmo formato de
    // `getSubscriptionStatus`, com o plano preenchido — aplicá-la direto evita
    // uma segunda ida ao servidor e o piscar de "carregando" numa tela que o
    // usuário está olhando logo depois de confirmar algo delicado.
    const atualizada = await cancelSubscription(apiClient);
    setEstado((anterior) => ({ ...anterior, assinatura: atualizada }));
  }, []);

  return { ...estado, recarregar: carregar, assinar, cancelar };
}
