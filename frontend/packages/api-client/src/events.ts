import type { Address, AddressPayload } from './addresses';
import type { ApiClient } from './client';

/**
 * Agenda — espelha `EventEndpoints` no backend.
 *
 * Instantes trafegam em ISO 8601 **com offset**, e o servidor normaliza para
 * UTC. Mandar horário sem fuso faria o mesmo culto cair em horas diferentes
 * conforme o aparelho de quem cadastrou.
 */

export type EventStatus = 'Agendado' | 'Cancelado';

/**
 * O tipo do evento como a agenda precisa dele: nome e ícone, já resolvidos.
 *
 * O servidor manda o objeto aninhado, e não só o identificador, porque a lista
 * desenha o nome e o ícone de cada linha — com só o `id` o app teria de cruzar
 * com a lista de tipos a cada render, ou fazer uma segunda requisição para
 * mostrar uma palavra.
 *
 * Recorte de `EventType` (em `eventTypes.ts`): sem `isActive` e sem
 * `eventCount`, que dizem respeito a administrar o vocabulário e não a desenhar
 * um evento.
 */
export interface EventTypeRef {
  readonly id: string;
  readonly name: string;
  readonly icon: string;

  /**
   * Cor do tipo, `#RRGGBB`, ou `null` para a cor padrão.
   *
   * **Nunca sozinha.** Na agenda ela aparece na faixa da linha e no ponto do
   * filtro, sempre ao lado do nome do tipo escrito — uma cor sem rótulo não
   * diria nada a quem não distingue matiz.
   */
  readonly colorHex: string | null;
}

export interface CalendarEvent {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly location: string | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly status: EventStatus;
  /** `null` quando o evento não foi classificado — estado legítimo, não erro. */
  readonly type: EventTypeRef | null;

  /**
   * Endereço do evento, ou `null`.
   *
   * Convive com `location`: aquele é o nome do lugar como a igreja o chama
   * ("Templo", "Chácara do irmão João"), este é onde fica.
   */
  readonly address: Address | null;
}

export interface SaveEventInput {
  readonly title: string;

  /**
   * Identificador do tipo, ou ausente para evento sem classificação.
   *
   * Na **edição**, ausente significa "não mexa no tipo" — quem edita só o
   * horário não deve desclassificar o evento por omissão. Para remover a
   * classificação existe `clearType`, porque sem essa distinção os dois pedidos
   * chegariam idênticos ao servidor.
   */
  readonly typeId?: string;

  /** Remove a classificação do evento. Só faz sentido na edição. */
  readonly clearType?: boolean;

  /** Endereço do evento. Ausente na edição significa "não mexa". */
  readonly address?: AddressPayload;

  readonly description?: string;
  readonly location?: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface ListEventsInput {
  readonly from: string;
  readonly to: string;
  readonly includeCanceled?: boolean;
}

export function listEvents(
  client: ApiClient,
  input: ListEventsInput,
  signal?: AbortSignal,
): Promise<readonly CalendarEvent[]> {
  const query = new URLSearchParams({ from: input.from, to: input.to });
  if (input.includeCanceled !== undefined) {
    query.set('includeCanceled', String(input.includeCanceled));
  }

  return client.request<readonly CalendarEvent[]>(`/api/v1/events?${query.toString()}`, {
    ...(signal ? { signal } : {}),
  });
}

export function listUpcomingEvents(
  client: ApiClient,
  limit = 5,
  signal?: AbortSignal,
): Promise<readonly CalendarEvent[]> {
  return client.request<readonly CalendarEvent[]>(`/api/v1/events/upcoming?limit=${limit}`, {
    ...(signal ? { signal } : {}),
  });
}

export function getEvent(client: ApiClient, id: string): Promise<CalendarEvent> {
  return client.request<CalendarEvent>(`/api/v1/events/${id}`);
}

export function createEvent(client: ApiClient, input: SaveEventInput): Promise<CalendarEvent> {
  return client.request<CalendarEvent>('/api/v1/events', { method: 'POST', body: input });
}

export function updateEvent(
  client: ApiClient,
  id: string,
  input: SaveEventInput,
): Promise<CalendarEvent> {
  return client.request<CalendarEvent>(`/api/v1/events/${id}`, { method: 'PUT', body: input });
}

export function cancelEvent(client: ApiClient, id: string): Promise<CalendarEvent> {
  return client.request<CalendarEvent>(`/api/v1/events/${id}/cancel`, { method: 'PUT' });
}

export function reactivateEvent(client: ApiClient, id: string): Promise<CalendarEvent> {
  return client.request<CalendarEvent>(`/api/v1/events/${id}/reactivate`, { method: 'PUT' });
}

export function deleteEvent(client: ApiClient, id: string): Promise<void> {
  return client.request<void>(`/api/v1/events/${id}`, { method: 'DELETE' });
}
