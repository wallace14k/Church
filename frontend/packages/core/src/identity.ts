/**
 * Contratos de identidade, espelhando o backend.
 *
 * A regra que organiza tudo, e que este arquivo precisa refletir sem
 * ambiguidade: **identidade é global, pertencimento é contextual, direito de
 * acesso é resolvido à parte**. Ver `CLAUDE.md` na raiz do repositório.
 */

/** Papéis de sistema. Espelha `SystemRoles` em `Congrega.Domain.Tenancy`. */
export const ROLES = {
  churchAdmin: 'ChurchAdmin',
  treasurer: 'Treasurer',
  cellLeader: 'CellLeader',
  childcareStaff: 'ChildcareStaff',
  member: 'Member',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

/** Permissões atômicas. Espelha `Permissions` em `Congrega.Domain.Tenancy`. */
export const PERMISSIONS = {
  membersRead: 'members.read',
  membersWrite: 'members.write',
  givingRead: 'giving.read',
  givingWrite: 'giving.write',
  childrenRead: 'children.read',
  childrenCheckIn: 'children.checkin',
  childrenCheckout: 'children.checkout',
  eventsWrite: 'events.write',
  billingManage: 'billing.manage',
} as const;

export type Permission = (typeof PERMISSIONS)[keyof typeof PERMISSIONS];

/**
 * Sessão autenticada.
 *
 * `tenantId` ausente **não** é erro nem estado degradado: é o assinante
 * Congrega+ sem vínculo com igreja, que é cidadão de primeira classe do produto.
 * Toda tela precisa funcionar nesse estado ou dizer claramente que exige igreja.
 */
export interface Session {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly userId: string;
  readonly tenantId: string | null;
  readonly roles: readonly Role[];
}

/** Igreja disponível para o usuário selecionar. */
export interface TenantOption {
  readonly id: string;
  readonly name: string;
}

/**
 * Verifica papel.
 *
 * Serve para **decidir o que mostrar**, nunca para proteger dado. A autorização
 * de verdade acontece no servidor; esconder um botão no cliente é usabilidade,
 * não segurança — quem tiver o token pode chamar o endpoint direto.
 */
export function hasRole(session: Session | null, role: Role): boolean {
  return session?.roles.includes(role) ?? false;
}

export function hasAnyRole(session: Session | null, roles: readonly Role[]): boolean {
  return roles.some((role) => hasRole(session, role));
}

/** Indica se a sessão está operando dentro de uma igreja. */
export function isTenantScoped(session: Session | null): session is Session & { tenantId: string } {
  return typeof session?.tenantId === 'string';
}

/**
 * Diz se o access token está perto demais do vencimento para ser usado.
 *
 * A margem de 30 segundos existe porque o token pode vencer no voo: enviar um
 * token que expira em 2 segundos gera um 401 evitável e um retry que o usuário
 * percebe como lentidão.
 */
export function needsRefresh(session: Session | null, now: Date = new Date()): boolean {
  if (session === null) return false;

  const expiresAt = Date.parse(session.expiresAt);
  if (Number.isNaN(expiresAt)) return true;

  return expiresAt - now.getTime() <= 30_000;
}
