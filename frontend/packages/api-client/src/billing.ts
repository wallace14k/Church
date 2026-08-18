import type { ApiClient } from './client';

/**
 * Cobrança do Congrega+ — espelha `BillingEndpoints` no backend.
 *
 * Assinatura B2C, independente de igreja: os dois endpoints de leitura vivem
 * fora de qualquer escopo de tenant, e é por isso que nenhuma função aqui
 * pede `tenantId`.
 */

export type SubscriptionState =
  | 'Pending'
  | 'Active'
  | 'PastDue'
  | 'Grace'
  | 'Canceled'
  | 'Expired';

export interface SubscriptionStatus {
  readonly hasSubscription: boolean;
  readonly planCode: string | null;
  readonly planName: string | null;
  readonly status: SubscriptionState | null;
  readonly currentPeriodEnd: string | null;
  readonly graceUntil: string | null;
  readonly cancelAtPeriodEnd: boolean;
}

export interface Plan {
  readonly code: string;
  readonly name: string;
  /** Centavos, nunca reais decimais — ver `@congrega/core/money`. */
  readonly priceCents: number;
  /** 1=Mensal 2=Anual. */
  readonly billingPeriod: 1 | 2;
}

export interface CheckoutResult {
  readonly paymentId: string;
  readonly amountCents: number;
  readonly status: string;
  readonly planName: string | null;
  readonly checkoutUrl: string | null;
  readonly pixCode: string | null;
  /** A chave já tinha sido usada — esta é a MESMA cobrança da tentativa anterior. */
  readonly reused: boolean;
}

export function getSubscriptionStatus(
  client: ApiClient,
  signal?: AbortSignal,
): Promise<SubscriptionStatus> {
  return client.request<SubscriptionStatus>('/api/v1/billing/subscription', {
    ...(signal ? { signal } : {}),
  });
}

export function listPlans(client: ApiClient, signal?: AbortSignal): Promise<readonly Plan[]> {
  return client.request<readonly Plan[]>('/api/v1/billing/plans', {
    ...(signal ? { signal } : {}),
  });
}

export function startCheckout(
  client: ApiClient,
  planCode: string,
  idempotencyKey: string,
): Promise<CheckoutResult> {
  return client.request<CheckoutResult>('/api/v1/billing/checkout', {
    method: 'POST',
    body: { planCode },
    headers: { 'Idempotency-Key': idempotencyKey },
  });
}
