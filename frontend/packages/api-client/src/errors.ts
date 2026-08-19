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

/** Natureza da falha. Decide título, tom e se faz sentido tentar de novo. */
export type FailureKind = 'offline' | 'forbidden' | 'server' | 'client';

/**
 * Falha já classificada, pronta para a interface decidir o que mostrar.
 *
 * Existe porque `describeError` devolve **só texto**, e texto perde a
 * categoria. Quem recebia a string não tinha como saber que um 403 não é uma
 * falha de carregamento — e o resultado, na tela, era um título dizendo "não
 * deu para carregar" sobre um problema de permissão, com um botão "Tentar de
 * novo" que ia falhar exatamente igual.
 */
export interface Failure {
  readonly kind: FailureKind;

  /**
   * Título próprio da categoria, ou `null` quando o título contextual da tela
   * ("Não deu para carregar a agenda") é o mais informativo.
   */
  readonly title: string | null;

  readonly description: string;

  /**
   * Vale oferecer "tentar de novo"?
   *
   * Espelha `ApiError.isRetryable`. Oferecer a ação onde ela não pode
   * funcionar é pior do que não oferecer: o usuário clica, nada muda, e conclui
   * que o produto está quebrado em vez de entender que falta permissão.
   */
  readonly canRetry: boolean;
}

/** Classifica a falha. Ver <see cref="Failure"/> para o porquê de não ser só texto. */
export function describeFailure(error: unknown): Failure {
  if (error instanceof NetworkError) {
    return {
      kind: 'offline',
      title: 'Sem conexão',
      description: 'Não foi possível falar com o servidor. Verifique a internet e tente de novo.',
      canRetry: true,
    };
  }

  if (error instanceof ApiError) {
    if (error.isForbidden) {
      return {
        kind: 'forbidden',
        title: 'Acesso não autorizado',
        description: describeError(error),
        canRetry: false,
      };
    }

    return {
      kind: error.status >= 500 || error.isRateLimited ? 'server' : 'client',
      // Sem título próprio: aqui o contexto da tela ("...a agenda") diz mais do
      // que um "erro do servidor" genérico.
      title: null,
      description: describeError(error),
      canRetry: error.isRetryable,
    };
  }

  return { kind: 'client', title: null, description: describeError(error), canRetry: true };
}
