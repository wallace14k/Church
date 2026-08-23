import type { Address, AddressPayload } from './addresses';
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

/**
 * Lacuna do cadastro a filtrar.
 *
 * `Any` é "perfil incompleto": falta telefone **ou** e-mail.
 */
export type MemberGap = 'Any' | 'SemTelefone' | 'SemEmail';

/**
 * Contagens do acervo, para os chips de filtro.
 *
 * **Do acervo inteiro, não da página.** Contar no cliente sobre os 50 itens
 * carregados diria "5 incompletos" numa igreja com 9 — e o número apareceria ao
 * lado de um filtro que devolve os 9.
 */
export interface MemberSummary {
  readonly total: number;
  readonly birthdayThisMonth: number;
  /** Sem telefone ou sem e-mail. */
  readonly incomplete: number;
  readonly withoutPhone: number;
  readonly withoutEmail: number;
}

export interface ListMembersInput {
  readonly search?: string;
  readonly page?: number;
  readonly pageSize?: number;
  readonly birthdayMonth?: number;
  readonly gap?: MemberGap;
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
  /**
   * Endereço do membro.
   *
   * Substitui os seis campos planos. Ausente na edição significa "não mexa";
   * um objeto com tudo em branco significa "apague".
   */
  readonly address?: AddressPayload;
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
  if (input.gap !== undefined) query.set('gap', input.gap);
  if (input.status !== undefined) query.set('status', input.status);

  const sufixo = query.size > 0 ? `?${query.toString()}` : '';

  return client.request<Paged<Member>>(`/api/v1/members${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

/**
 * Contagens para os chips de filtro da listagem.
 *
 * O mês dos aniversariantes vem do **servidor**: se o cliente o mandasse, dois
 * usuários em fusos diferentes veriam contagens diferentes para a mesma igreja.
 */
export function getMemberSummary(
  client: ApiClient,
  status?: MemberStatus | 'Todos',
  signal?: AbortSignal,
): Promise<MemberSummary> {
  const sufixo = status === undefined ? '' : `?status=${status}`;

  return client.request<MemberSummary>(`/api/v1/members/summary${sufixo}`, {
    ...(signal ? { signal } : {}),
  });
}

/**
 * O membro na tela de detalhe — tudo de `Member` mais o endereço.
 *
 * Tipo separado, e não um campo opcional em `Member`: a listagem não carrega
 * endereço de propósito (seriam N junções para desenhar uma tela que não o
 * mostra), e um `address: null` na lista seria indistinguível de "este membro
 * não tem endereço". O servidor espelha isso com `MemberDetailResponse`.
 */
export interface MemberDetail extends Member {
  readonly address: Address | null;
}

export function getMember(client: ApiClient, id: string): Promise<MemberDetail> {
  return client.request<MemberDetail>(`/api/v1/members/${id}`);
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
  /**
   * Endereço do membro.
   *
   * Substitui os seis campos planos. Ausente na edição significa "não mexa";
   * um objeto com tudo em branco significa "apague".
   */
  readonly address?: AddressPayload;
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
