import { describe, expect, it } from 'vitest';
import { ROLES, hasAnyRole, hasRole, isTenantScoped, needsRefresh, type Session } from './identity';

function session(overrides: Partial<Session> = {}): Session {
  return {
    accessToken: 'token',
    expiresAt: '2026-08-15T12:15:00Z',
    userId: 'a3f1e0c2-0000-4000-8000-000000000001',
    tenantId: '9c2b1d44-0000-4000-8000-000000000002',
    roles: [ROLES.member],
    ...overrides,
  };
}

describe('hasRole', () => {
  it('reconhece papel presente e ausente', () => {
    const admin = session({ roles: [ROLES.churchAdmin, ROLES.treasurer] });
    expect(hasRole(admin, ROLES.churchAdmin)).toBe(true);
    expect(hasRole(admin, ROLES.cellLeader)).toBe(false);
  });

  it('trata sessão nula sem quebrar', () => {
    // A tela renderiza antes de a sessão hidratar. Lançar aqui viraria tela
    // branca no primeiro frame de todo cold start.
    expect(hasRole(null, ROLES.churchAdmin)).toBe(false);
    expect(hasAnyRole(null, [ROLES.churchAdmin])).toBe(false);
  });
});

describe('isTenantScoped', () => {
  it('reconhece sessão com igreja', () => {
    expect(isTenantScoped(session())).toBe(true);
  });

  it('reconhece assinante sem igreja como estado válido', () => {
    // Não é erro nem estado degradado: é o assinante Congrega+, cidadão de
    // primeira classe do produto.
    const subscriber = session({ tenantId: null, roles: [] });
    expect(isTenantScoped(subscriber)).toBe(false);
    expect(subscriber.roles).toHaveLength(0);
  });
});

describe('needsRefresh', () => {
  it('pede renovação dentro da margem de segurança', () => {
    // Token que vence em 20s ainda é "válido", mas mandá-lo garante um 401 no
    // meio do caminho e um retry que o usuário sente como lentidão.
    const now = new Date('2026-08-15T12:14:40Z');
    expect(needsRefresh(session(), now)).toBe(true);
  });

  it('não pede renovação com folga', () => {
    const now = new Date('2026-08-15T12:05:00Z');
    expect(needsRefresh(session(), now)).toBe(false);
  });

  it('trata expiração ilegível como necessidade de renovar', () => {
    // Falha para o lado seguro: renovar à toa custa uma requisição; usar um
    // token que não dá para avaliar custa uma sessão quebrada.
    expect(needsRefresh(session({ expiresAt: 'lixo' }))).toBe(true);
  });

  it('não pede renovação quando não há sessão', () => {
    expect(needsRefresh(null)).toBe(false);
  });
});
