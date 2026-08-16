/**
 * Erros da API.
 *
 * O backend responde erro em `application/problem+json` (RFC 7807). Este módulo
 * traduz isso em tipos que a interface consegue tratar sem inspecionar strings.
 */

/** Corpo de erro conforme RFC 7807, com as extensões que o Congrega adiciona. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly instance?: string;
  readonly correlationId?: string;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;
  /** Identificador para o usuário citar ao suporte, e para achar o rastro no log. */
  readonly correlationId: string | null;

  constructor(status: number, problem: ProblemDetails | null, fallbackMessage: string) {
    super(problem?.detail ?? problem?.title ?? fallbackMessage);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.correlationId = problem?.correlationId ?? null;
  }

  /** Sessão inválida ou expirada — o cliente precisa voltar ao login. */
  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  /** Autenticado, mas sem permissão. Diferente de 401: refazer o login não resolve. */
  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isRateLimited(): boolean {
    return this.status === 429;
  }

  /**
   * Vale tentar de novo?
   *
   * Só para 5xx e 429. Repetir um 400 é gastar bateria e dados do usuário em
   * algo que vai falhar de novo exatamente igual.
   */
  get isRetryable(): boolean {
    return this.status >= 500 || this.status === 429;
  }
}

/** Falha antes de haver resposta: sem rede, DNS, TLS, timeout. */
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('Não foi possível falar com o servidor.');
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/**
 * Mensagem para exibir ao usuário.
 *
 * Erros não pedem desculpa e nunca são vagos sobre o que aconteceu — dizem o
 * que houve e o que fazer a respeito. O texto técnico fica no log.
 */
export function describeError(error: unknown): string {
  if (error instanceof NetworkError) {
    return 'Sem conexão com o servidor. Verifique a internet e tente de novo.';
  }

  if (error instanceof ApiError) {
    if (error.isRateLimited) {
      return 'Muitas tentativas. Aguarde alguns minutos antes de tentar de novo.';
    }
    if (error.isForbidden) {
      return 'Sua conta não tem acesso a esta área. Fale com o administrador da sua igreja.';
    }
    if (error.status >= 500) {
      return 'O servidor teve um problema. Tente de novo em instantes.';
    }
    return error.message;
  }

  return 'Algo saiu errado. Tente de novo.';
}
