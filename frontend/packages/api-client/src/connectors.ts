import type { ApiClient } from './client';

/**
 * Integrações — espelha `ConnectorEndpoints` no backend.
 *
 * **Nenhum tipo aqui tem campo de segredo de leitura**, e a ausência é
 * deliberada: a senha, o token e a chave nunca voltam do servidor. O que a tela
 * sabe é se existe um segredo guardado (`hasSecret`), que é tudo de que ela
 * precisa para desenhar "configurado" ou "faltando".
 */

export type ConnectorKind = 'Smtp' | 'Telegram' | 'GoogleDrive';

/** Porta 587 usa StartTls; porta 465 usa SslOnConnect. Não há opção sem TLS. */
export type SmtpSecurity = 'StartTls' | 'SslOnConnect';

export interface Connector {
  readonly id: string;
  readonly kind: ConnectorKind;
  readonly isEnabled: boolean;

  /** Configuração não-secreta. As chaves variam por tipo. */
  readonly settings: Readonly<Record<string, string>>;

  /** Há credencial guardada. A credencial em si nunca sai do servidor. */
  readonly hasSecret: boolean;

  readonly lastTestedAt: string | null;

  /**
   * `null` = nunca testado.
   *
   * Três estados, não dois: "ninguém sabe se funciona" não é a mesma coisa que
   * "sabemos que está quebrado", e a tela precisa dizer coisas diferentes.
   */
  readonly lastTestSucceeded: boolean | null;

  readonly lastTestMessage: string | null;
  readonly updatedAt: string;
}

export interface SaveConnectorInput {
  readonly settings: Readonly<Record<string, string>>;

  /**
   * A credencial. **Omitir mantém a que já está guardada.**
   *
   * A tela nunca recebe o segredo de volta, logo não tem como reenviá-lo. Se
   * omissão apagasse, corrigir a porta apagaria a senha junto.
   */
  readonly secret?: string;

  readonly isEnabled: boolean;
}

export interface ConnectorTestResult {
  readonly succeeded: boolean;
  readonly message: string;
  readonly testedAt: string;
}

export function listConnectors(
  client: ApiClient,
  signal?: AbortSignal,
): Promise<readonly Connector[]> {
  return client.request<readonly Connector[]>('/api/v1/connectors', {
    ...(signal ? { signal } : {}),
  });
}

export function saveConnector(
  client: ApiClient,
  kind: ConnectorKind,
  input: SaveConnectorInput,
): Promise<Connector> {
  return client.request<Connector>(`/api/v1/connectors/${kind}`, { method: 'PUT', body: input });
}

export function deleteConnector(client: ApiClient, kind: ConnectorKind): Promise<void> {
  return client.request<void>(`/api/v1/connectors/${kind}`, { method: 'DELETE' });
}

/**
 * Tenta falar com o serviço externo de verdade.
 *
 * **Resolve mesmo quando o teste reprova.** Credencial errada é o resultado
 * esperado desta chamada, não um erro de rede — o servidor devolve 200 com
 * `succeeded: false` e a explicação, e tratá-la pelo caminho de erro descartaria
 * justamente o diagnóstico que se foi buscar.
 */
export function testConnector(
  client: ApiClient,
  kind: ConnectorKind,
): Promise<ConnectorTestResult> {
  return client.request<ConnectorTestResult>(`/api/v1/connectors/${kind}/test`, {
    method: 'POST',
  });
}
