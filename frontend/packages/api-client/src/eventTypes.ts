import type { ApiClient } from './client';

/**
 * Um tipo de evento cadastrado pela igreja.
 *
 * Era um `enum` de cinco valores no cliente. Virou dado do servidor porque o
 * vocabulário da agenda pertence à igreja: uma congregação com "Vigília" e
 * "Célula" não tinha onde encaixá-las.
 */
export interface EventType {
  readonly id: string;
  readonly name: string;
  /** Nome do ícone no conjunto Feather — o servidor só aceita os que o app desenha. */
  readonly icon: string;

  /** Cor do tipo, `#RRGGBB`, ou `null` para a cor padrão do sistema. */
  readonly colorHex: string | null;
  readonly isActive: boolean;
  /**
   * Quantos eventos usam este tipo.
   *
   * Serve para **avisar** antes do clique em excluir, não para decidir se pode:
   * quem recusa é a chave estrangeira do banco. Conferir aqui e só então apagar
   * abriria uma janela entre a leitura e a exclusão.
   */
  readonly eventCount: number;
}

export interface SaveEventTypeInput {
  readonly name: string;
  readonly icon?: string;

  /** Ausente mantém a cor que já está gravada. */
  readonly colorHex?: string;
  readonly isActive?: boolean;
}

/**
 * Lista os tipos da igreja.
 *
 * `includeInactive` é o que a tela de administração usa: sem ele, desativar um
 * tipo o faria sumir da própria tela onde se reativa.
 */
export function listEventTypes(
  client: ApiClient,
  includeInactive = false,
): Promise<readonly EventType[]> {
  const busca = includeInactive ? '?includeInactive=true' : '';
  return client.request<readonly EventType[]>(`/api/v1/event-types${busca}`);
}

/**
 * Ícones que o servidor aceita.
 *
 * Vem da API em vez de uma constante local de propósito: duas listas
 * divergiriam, e o app ofereceria um ícone que o servidor recusa — o usuário
 * levaria um 400 depois de escolher.
 */
export function listEventTypeIcons(client: ApiClient): Promise<readonly string[]> {
  return client.request<readonly string[]>('/api/v1/event-types/icons');
}

export function createEventType(
  client: ApiClient,
  input: SaveEventTypeInput,
): Promise<EventType> {
  return client.request<EventType>('/api/v1/event-types', { method: 'POST', body: input });
}

export function updateEventType(
  client: ApiClient,
  id: string,
  input: SaveEventTypeInput,
): Promise<EventType> {
  return client.request<EventType>(`/api/v1/event-types/${id}`, { method: 'PUT', body: input });
}

/**
 * Exclui um tipo que nunca foi usado.
 *
 * Responde 409 quando há eventos classificados nele — a saída nesse caso é
 * desativar, que tira o tipo do formulário sem apagar a classificação da agenda
 * passada.
 */
export function deleteEventType(client: ApiClient, id: string): Promise<void> {
  return client.request<void>(`/api/v1/event-types/${id}`, { method: 'DELETE' });
}
