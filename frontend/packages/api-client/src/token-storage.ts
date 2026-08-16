/**
 * Guarda do refresh token.
 *
 * O contrato é o mesmo nas três plataformas; a implementação diverge, e a
 * divergência fica confinada aos arquivos `.native.ts` e `.web.ts` — o Metro
 * escolhe qual carregar pela extensão. Nenhum outro ponto do código sabe onde o
 * token mora.
 *
 * **O access token nunca passa por aqui.** Ele vive 15 minutos e fica só em
 * memória: persistir cria superfície de ataque sem benefício, já que ao reabrir
 * o app o refresh reidrata a sessão de qualquer forma.
 *
 * **Nunca AsyncStorage.** É texto plano no sandbox do app, legível em aparelho
 * comprometido e em backup não criptografado.
 */
export interface TokenStorage {
  read(): Promise<string | null>;
  write(token: string): Promise<void>;
  clear(): Promise<void>;
}

/** Chave única. Trocar quebra a sessão de quem já está logado. */
export const REFRESH_TOKEN_KEY = 'congrega.refresh_token';

/**
 * Guarda em memória, para teste e para o web.
 *
 * No navegador o refresh token viaja em cookie `HttpOnly` definido pelo
 * servidor — o JavaScript não o alcança, que é exatamente o ponto. Esta
 * implementação existe para satisfazer o contrato sem guardar nada.
 */
export class InMemoryTokenStorage implements TokenStorage {
  #token: string | null = null;

  read(): Promise<string | null> {
    return Promise.resolve(this.#token);
  }

  write(token: string): Promise<void> {
    this.#token = token;
    return Promise.resolve();
  }

  clear(): Promise<void> {
    this.#token = null;
    return Promise.resolve();
  }
}
