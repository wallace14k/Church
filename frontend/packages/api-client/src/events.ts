import type { ApiClient } from './client';

/**
 * Agenda — espelha `EventEndpoints` no backend.
 *
 * Instantes trafegam em ISO 8601 **com offset**, e o servidor normaliza para
 * UTC. Mandar horário sem fuso faria o mesmo culto cair em horas diferentes
 * conforme o aparelho de quem cadastrou.
 */

export type EventStatus = 'Agendado' | 'Cancelado';

export interface CalendarEvent {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly location: string | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly status: EventStatus;
}

export interface SaveEventInput {
  readonly title: string;
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
