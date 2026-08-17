import type { ApiClient } from './client';

/**
 * Membros da igreja.
 *
 * Espelha `MemberEndpoints` no backend. Os tipos são escritos à mão em vez de
 * gerados: são poucos e estáveis, e um gerador traria uma etapa de build para
 * economizar trinta linhas. Se o contrato crescer, vale reavaliar.
 */

export type MemberStatus = 'Ativo' | 'Inativo' | 'Transferido' | 'Falecido';

export interface Member {
  readonly id: string;
  readonly fullName: string;
  readonly email: string | null;
  readonly phone: string | null;
  readonly birthDate: string | null;
  readonly age: number | null;
  readonly status: MemberStatus;
  readonly familyName: string | null;
}

export interface Paged<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasNext: boolean;
}

export interface ListMembersInput {
  readonly search?: string;
  readonly page?: number;
  readonly pageSize?: number;
  readonly birthdayMonth?: number;
  /** `Todos` remove o filtro. Sem valor, o servidor devolve só os ativos. */
  readonly status?: MemberStatus | 'Todos';
}

export interface CreateMemberInput {
  readonly fullName: string;
  readonly email?: string;
  readonly phone?: string;
  readonly birthDate?: string;
  readonly gender?: 1 | 2 | 3;
  readonly maritalStatus?: 1 | 2 | 3 | 4 | 5;
  readonly membershipDate?: string;
  readonly baptismDate?: string;
  readonly addressStreet?: string;
  readonly addressNumber?: string;
  readonly addressDistrict?: string;
  readonly addressCity?: string;
  readonly addressState?: string;
  readonly addressZip?: string;
  readonly notes?: string;
}

export async function listMembers(
  client: ApiClient,
  input: ListMembersInput = {},
  signal?: AbortSignal,
): Promise<Paged<Member>> {
  const query = new URLSearchParams();

  // Só os parâmetros informados entram na URL. Mandar `search=` vazio faria o
  // servidor tratar como busca por string vazia em vez de "sem filtro".
  if (input.search !== undefined && input.search.trim().length > 0) {
    query.set('search', input.search.trim());
  }
  if (input.page !== undefined) query.set('page', String(input.page));
  if (input.pageSize !== undefined) query.set('pageSize', String(input.pageSize));
  if (input.birthdayMonth !== undefined) query.set('birthdayMonth', String(input.birthdayMonth));
  if (input.status !== undefined) query.set('status', input.status);

  const sufixo = query.size > 0 ? `?${query.toString()}` : '';

  return client.request<Paged<Member>>(`/api/v1/members${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

export function getMember(client: ApiClient, id: string): Promise<Member> {
  return client.request<Member>(`/api/v1/members/${id}`);
}

export function createMember(client: ApiClient, input: CreateMemberInput): Promise<Member> {
  return client.request<Member>('/api/v1/members', { method: 'POST', body: input });
}

/**
 * Campos editáveis da ficha — subconjunto de {@link CreateMemberInput}.
 *
 * Espelha `UpdateMemberRequest` no backend: gênero, estado civil e as datas de
 * vínculo/batismo ainda não têm campo na tela, e adicioná-los aqui sem a tela
 * correspondente reabriria o problema que o `TODO.md` existe para evitar.
 */
export interface UpdateMemberInput {
  readonly fullName: string;
  readonly email?: string;
  readonly phone?: string;
  readonly birthDate?: string;
  readonly addressStreet?: string;
  readonly addressNumber?: string;
  readonly addressDistrict?: string;
  readonly addressCity?: string;
  readonly addressState?: string;
  readonly addressZip?: string;
}

export function updateMember(client: ApiClient, id: string, input: UpdateMemberInput): Promise<Member> {
  return client.request<Member>(`/api/v1/members/${id}`, { method: 'PUT', body: input });
}

export function changeMemberStatus(client: ApiClient, id: string, status: MemberStatus): Promise<Member> {
  return client.request<Member>(`/api/v1/members/${id}/status`, {
    method: 'PUT',
    body: { status },
  });
}

/** `familyId: null` desvincula o membro de qualquer família. */
export function assignMemberFamily(
  client: ApiClient,
  id: string,
  familyId: string | null,
): Promise<Member> {
  return client.request<Member>(`/api/v1/members/${id}/family`, {
    method: 'PUT',
    body: { familyId },
  });
}

/** Uma linha de planilha já mapeada para os campos do cadastro. */
export interface ImportMemberRow {
  readonly fullName: string;
  readonly email?: string;
  readonly phone?: string;
  readonly birthDate?: string;
  readonly addressCity?: string;
}

export interface ImportRowIssue {
  /** Posição na lista enviada, 1-based — a mesma numeração mostrada na tela. */
  readonly row: number;
  readonly reason: string;
}

export interface ImportMembersResult {
  readonly imported: number;
  readonly skipped: number;
  readonly issues: readonly ImportRowIssue[];
}

export function importMembers(
  client: ApiClient,
  rows: readonly ImportMemberRow[],
): Promise<ImportMembersResult> {
  return client.request<ImportMembersResult>('/api/v1/members/import', {
    method: 'POST',
    body: { rows },
  });
}
