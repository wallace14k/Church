import type { ApiClient, Session } from './client';

/**
 * Endpoints de autenticação.
 *
 * Todos marcados como `anonymous`: são eles que **criam** a sessão, então
 * disparar renovação antes deles seria circular.
 */

export interface RequestOtpInput {
  readonly email: string;
  readonly fullName?: string;
}

export interface VerifyOtpInput {
  readonly email: string;
  readonly code: string;
  readonly tenantId?: string;
  readonly deviceLabel?: string;
}

/**
 * Pede um código de acesso.
 *
 * **Sempre resolve.** O servidor responde `202` mesmo para e-mail inexistente,
 * bloqueado ou que estourou o limite — qualquer diferenciação transformaria o
 * endpoint em oráculo de enumeração de usuários. A interface reflete isso: não
 * há retorno a inspecionar, e a tela sempre avança para a de código.
 */
export async function requestOtp(client: ApiClient, input: RequestOtpInput): Promise<void> {
  await client.request<void>('/api/v1/auth/otp/request', {
    method: 'POST',
    body: { email: input.email.trim(), fullName: input.fullName },
    anonymous: true,
  });
}

/**
 * Valida o código e abre a sessão.
 *
 * Falha com `ApiError` 400 e mensagem única para todos os casos — código
 * errado, expirado, tentativas esgotadas, usuário inexistente. Não tente
 * inferir o motivo a partir do texto: ele é deliberadamente o mesmo.
 */
export async function verifyOtp(client: ApiClient, input: VerifyOtpInput): Promise<Session> {
  const session = await client.request<Session>('/api/v1/auth/otp/verify', {
    method: 'POST',
    body: {
      email: input.email.trim(),
      code: input.code,
      tenantId: input.tenantId,
      deviceLabel: input.deviceLabel,
    },
    anonymous: true,
  });

  client.setSession(session);
  return session;
}

/**
 * Troca a igreja ativa sem refazer o login.
 *
 * Aproveita a rotação do refresh token e preserva a family — emitir uma family
 * nova a cada troca de contexto fragmentaria o rastreamento e criaria sessões
 * órfãs que a detecção de reuso não cobriria.
 */
export async function switchTenant(client: ApiClient, tenantId: string): Promise<Session> {
  const session = await client.request<Session>('/api/v1/auth/refresh', {
    method: 'POST',
    body: { switchToTenantId: tenantId },
    anonymous: true,
  });

  client.setSession(session);
  return session;
}
