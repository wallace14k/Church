import type { ApiClient } from './client';
import type { Paged } from './members';

/**
 * Financeiro — espelha `GivingEndpoints` no backend.
 *
 * Todo valor trafega em **centavos inteiros**, nunca em reais decimais. Ver
 * `@congrega/core/money`: `0.1 + 0.2 !== 0.3`, e num livro-caixa de igreja esse
 * erro vira diferença de fechamento que alguém precisa explicar em assembleia.
 */

export type GivingKind = 'Entrada' | 'Saida';

export type GivingMethod =
  | 'Dinheiro'
  | 'Pix'
  | 'Cartao'
  | 'Transferencia'
  | 'Cheque'
  | 'Outro';

export interface GivingCategory {
  readonly id: string;
  readonly name: string;
  readonly kind: GivingKind;
  readonly isActive: boolean;
}

export interface GivingEntry {
  readonly id: string;
  readonly categoryName: string;
  readonly kind: GivingKind;
  /** Centavos, sempre positivo. O sinal vem de `kind`. */
  readonly amountCents: number;
  readonly occurredOn: string;
  readonly method: GivingMethod;
  readonly memberName: string | null;
  readonly notes: string | null;
}

export interface ClosingLine {
  readonly categoryId: string;
  readonly categoryName: string;
  readonly kind: GivingKind;
  readonly totalCents: number;
  readonly entryCount: number;
}

export interface MonthlyClosing {
  readonly year: number;
  readonly month: number;
  readonly totalIncomeCents: number;
  readonly totalExpenseCents: number;
  /** Entradas menos saídas. Negativo é informação, não erro. */
  readonly balanceCents: number;
  readonly lines: readonly ClosingLine[];
}

export interface ListGivingEntriesInput {
  readonly year?: number;
  readonly month?: number;
  readonly categoryId?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface CreateGivingEntryInput {
  readonly categoryId: string;
  readonly amountCents: number;
  readonly occurredOn: string;
  readonly method: GivingMethod;
  readonly memberId?: string;
  readonly notes?: string;
}

export function listGivingCategories(
  client: ApiClient,
  includeInactive = false,
  signal?: AbortSignal,
): Promise<readonly GivingCategory[]> {
  const sufixo = includeInactive ? '?includeInactive=true' : '';
  return client.request<readonly GivingCategory[]>(`/api/v1/giving/categories${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createGivingCategory(
  client: ApiClient,
  name: string,
  kind: GivingKind,
): Promise<GivingCategory> {
  return client.request<GivingCategory>('/api/v1/giving/categories', {
    method: 'POST',
    body: { name, kind },
  });
}

export function updateGivingCategory(
  client: ApiClient,
  id: string,
  name: string,
  isActive: boolean,
): Promise<GivingCategory> {
  return client.request<GivingCategory>(`/api/v1/giving/categories/${id}`, {
    method: 'PUT',
    body: { name, isActive },
  });
}

export function listGivingEntries(
  client: ApiClient,
  input: ListGivingEntriesInput = {},
  signal?: AbortSignal,
): Promise<Paged<GivingEntry>> {
  const query = new URLSearchParams();

  if (input.year !== undefined) query.set('year', String(input.year));
  if (input.month !== undefined) query.set('month', String(input.month));
  if (input.categoryId !== undefined) query.set('categoryId', input.categoryId);
  if (input.page !== undefined) query.set('page', String(input.page));
  if (input.pageSize !== undefined) query.set('pageSize', String(input.pageSize));

  const sufixo = query.size > 0 ? `?${query.toString()}` : '';

  return client.request<Paged<GivingEntry>>(`/api/v1/giving/entries${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function createGivingEntry(
  client: ApiClient,
  input: CreateGivingEntryInput,
): Promise<GivingEntry> {
  return client.request<GivingEntry>('/api/v1/giving/entries', { method: 'POST', body: input });
}

export function deleteGivingEntry(client: ApiClient, id: string): Promise<void> {
  return client.request<void>(`/api/v1/giving/entries/${id}`, { method: 'DELETE' });
}

export function getMonthlyClosing(
  client: ApiClient,
  year: number,
  month: number,
  signal?: AbortSignal,
): Promise<MonthlyClosing> {
  return client.request<MonthlyClosing>(`/api/v1/giving/closing?year=${year}&month=${month}`, {
    ...(signal ? { signal } : {}),
  });
}
