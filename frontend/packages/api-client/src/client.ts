import { ApiError, NetworkError, type ProblemDetails } from './errors';
import type { TokenStorage } from './token-storage';

export interface Session {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly refreshToken: string | null;
  readonly userId: string;
  readonly tenantId: string | null;
  readonly roles: readonly string[];
}

export interface ApiClientOptions {
  readonly baseUrl: string;
  readonly storage: TokenStorage;
  /**
   * `mobile` faz o servidor devolver o refresh token no corpo; qualquer outro
   * valor faz o servidor usar cookie `HttpOnly`. Quem decide o formato é o
   * servidor — deixar o cliente escolher permitiria a um XSS pedir justamente a
   * variante que o JavaScript consegue ler.
   */
  readonly clientKind: 'mobile' | 'web';
  /** Notifica a aplicação de que a sessão caiu de vez e é preciso refazer o login. */
  readonly onSessionEnded?: () => void;
}

interface RequestOptions {
  readonly method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  readonly body?: unknown;
  /** Pula a renovação automática. Usado pelos endpoints de autenticação. */
  readonly anonymous?: boolean;
  readonly signal?: AbortSignal;
  /**
   * Cabeçalhos extras — hoje só `Idempotency-Key` (checkout). Mesclados ANTES
   * de `Content-Type`/`Authorization` no corpo de `request()`, de propósito:
   * um valor aqui nunca sobrescreve os dois, só acrescenta.
   */
  readonly headers?: Record<string, string>;
}

/**
 * Cliente HTTP da API Congrega.
 *
 * Responsável por três coisas que, feitas errado, aparecem como bug intermitente
 * e difícil de reproduzir: anexar o token certo, renovar a sessão sem duplicar
 * requisição, e propagar o identificador de correlação.
 */
export class ApiClient {
  #session: Session | null = null;
  /** Promessa da renovação em curso. Ver `#ensureFreshSession`. */
  #refreshInFlight: Promise<Session | null> | null = null;

  constructor(private readonly options: ApiClientOptions) {}

  get session(): Session | null {
    return this.#session;
  }

  setSession(session: Session | null): void {
    this.#session = session;
  }

  /**
   * Hidrata a sessão a partir do que já está guardado — cookie `HttpOnly` no
   * web, refresh token do storage no nativo — sem exigir uma sessão anterior
   * em memória. É o que permite abrir o app já logado.
   *
   * **Não existe atalho aqui por acaso.** A tentação óbvia é chamar
   * `request('/api/v1/auth/refresh', { anonymous: true })` direto: a resposta
   * HTTP vem 200 com uma sessão válida, mas `request()` sozinho não adota
   * nada — só `#refresh()` chama `#adoptSession()`. Um chamador que descarta o
   * retorno de `request()` e depois lê `this.session` sempre encontra `null`,
   * mesmo com a renovação tendo funcionado no servidor: a app conclui
   * "anônimo" com uma sessão válida sentada no cookie. Foi exatamente esse o
   * bug — a hidratação do navegador nunca autenticava de verdade, silenciosa,
   * porque o sintoma (parece deslogado) é indistinguível de "realmente sem
   * sessão".
   *
   * Passar por `#ensureFreshSession()`, e não chamar `#refresh()` direto,
   * importa igualmente: outro componente montando ao mesmo tempo (ex.: uma
   * tela que já dispara sua própria consulta autenticada) cai no mesmo
   * `#refreshInFlight` em vez de abrir uma segunda renovação concorrente —
   * que rotacionaria o token duas vezes e a segunda seria lida como reuso.
   */
  async hydrateSession(): Promise<Session | null> {
    await this.#ensureFreshSession();
    return this.#session;
  }

  async request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const { method = 'GET', body, anonymous = false, signal } = options;

    if (!anonymous) {
      await this.#ensureFreshSession();
    }

    const headers: Record<string, string> = {
      Accept: 'application/json',
      'X-Congrega-Client': this.options.clientKind,
      'X-Correlation-Id': createCorrelationId(),
      ...options.headers,
    };

    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    if (!anonymous && this.#session !== null) {
      headers.Authorization = `Bearer ${this.#session.accessToken}`;
    }

    const response = await this.#fetch(path, {
      method,
      headers,
      // Propriedades opcionais são OMITIDAS, não postas como undefined:
      // `exactOptionalPropertyTypes` distingue as duas coisas, e com razão —
      // `{ body: undefined }` e `{}` não são equivalentes para o fetch.
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      // Necessário para o cookie HttpOnly do refresh no navegador. Em nativo é
      // ignorado, então pode ser incondicional.
      credentials: 'include',
      ...(signal ? { signal } : {}),
    });

    // 401 depois de já ter renovado significa que a sessão morreu de verdade —
    // token revogado, conta bloqueada, reuso detectado. Insistir aqui viraria
    // laço infinito de refresh.
    if (response.status === 401 && !anonymous) {
      await this.#endSession();
      throw new ApiError(401, await readProblem(response), 'Sessão expirada.');
    }

    if (!response.ok) {
      throw new ApiError(response.status, await readProblem(response), 'Falha na requisição.');
    }

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return undefined as T;
    }

    return (await response.json()) as T;
  }

  /**
   * Garante um access token utilizável antes de enviar a requisição.
   *
   * **Renovação em voo única.** Ao abrir o app, cinco telas disparam requisições
   * simultâneas com o token vencido. Sem coordenação, seriam cinco chamadas a
   * `/auth/refresh` — e como o refresh **rotaciona** o token, quatro delas
   * apresentariam um token já consumido. O servidor interpretaria isso como
   * reuso, revogaria a family inteira e derrubaria o usuário. O bug se
   * manifesta como "o app me desloga sozinho de vez em quando", que é
   * praticamente impossível de reproduzir sob demanda.
   *
   * Guardar a promessa em curso faz as demais aguardarem o mesmo resultado.
   */
  async #ensureFreshSession(): Promise<void> {
    if (this.#session !== null && !isExpiring(this.#session)) {
      return;
    }

    this.#refreshInFlight ??= this.#refresh().finally(() => {
      this.#refreshInFlight = null;
    });

    await this.#refreshInFlight;
  }

  async #refresh(): Promise<Session | null> {
    const stored = await this.options.storage.read();

    // No web não há token guardado: ele viaja em cookie HttpOnly, e a chamada
    // precisa acontecer mesmo sem corpo.
    if (stored === null && this.options.clientKind === 'mobile') {
      await this.#endSession();
      return null;
    }

    try {
      const refreshed = await this.request<Session>('/api/v1/auth/refresh', {
        method: 'POST',
        body: stored === null ? {} : { refreshToken: stored },
        anonymous: true,
      });

      await this.#adoptSession(refreshed);
      return refreshed;
    } catch (error) {
      if (error instanceof ApiError && error.isUnauthorized) {
        await this.#endSession();
        return null;
      }
      // Falha de rede não é sessão inválida. Derrubar o login porque o metrô
      // entrou num túnel seria hostil — e o token continua valendo.
      throw error;
    }
  }

  async #adoptSession(session: Session): Promise<void> {
    this.#session = session;

    if (session.refreshToken !== null) {
      await this.options.storage.write(session.refreshToken);
    }
  }

  async #endSession(): Promise<void> {
    this.#session = null;
    await this.options.storage.clear();
    this.options.onSessionEnded?.();
  }

  async #fetch(path: string, init: RequestInit): Promise<Response> {
    try {
      return await fetch(`${this.options.baseUrl}${path}`, init);
    } catch (error) {
      // fetch só rejeita por falha de transporte; erro HTTP vem como resposta.
      // Distinguir os dois é o que permite decidir entre "tente de novo" e
      // "verifique sua conexão".
      throw new NetworkError(error);
    }
  }
}

/** Margem de 30s: token que vence no voo geraria um 401 evitável. */
function isExpiring(session: Session): boolean {
  const expiresAt = Date.parse(session.expiresAt);
  return Number.isNaN(expiresAt) || expiresAt - Date.now() <= 30_000;
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    const contentType = response.headers.get('content-type') ?? '';
    if (!contentType.includes('json')) return null;
    return (await response.json()) as ProblemDetails;
  } catch {
    // Corpo ilegível não pode mascarar o status HTTP, que é a informação que
    // realmente importa para decidir o que fazer.
    return null;
  }
}

/**
 * Identificador de correlação por requisição.
 *
 * Ordenável por tempo, como um ULID: o prefixo é o instante em base 36. Isso faz
 * os identificadores de uma mesma sessão aparecerem em ordem no log, o que é
 * exatamente o que se quer ao investigar um relato de usuário.
 */
function createCorrelationId(): string {
  const timestamp = Date.now().toString(36).padStart(9, '0');
  const random = Math.random().toString(36).slice(2, 12).padEnd(10, '0');
  return `${timestamp}${random}`;
}
