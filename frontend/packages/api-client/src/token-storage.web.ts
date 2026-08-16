import type { TokenStorage } from './token-storage';

/**
 * Storage do navegador — deliberadamente vazio.
 *
 * No web o refresh token **não passa pelo JavaScript**. O servidor o entrega em
 * cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`, e o navegador
 * o reenvia sozinho nas chamadas de autenticação. É por isso que `read()`
 * devolve `null` e `write()` descarta: não há nada para guardar.
 *
 * A alternativa — `localStorage` — é legível por qualquer XSS. Um único script
 * injetado por uma dependência comprometida exfiltraria a sessão de 30 dias de
 * todos os usuários. O cookie `HttpOnly` torna esse ataque impossível pela via
 * do JavaScript.
 *
 * A classe existe só para satisfazer o contrato `TokenStorage`, mantendo o
 * `ApiClient` sem nenhum `if (Platform.OS === 'web')` no meio da lógica.
 */
export class CookieTokenStorage implements TokenStorage {
  read(): Promise<string | null> {
    // Sempre null: o cookie é invisível para o JS, por construção.
    return Promise.resolve(null);
  }

  write(): Promise<void> {
    // O servidor já definiu o cookie no Set-Cookie da mesma resposta.
    return Promise.resolve();
  }

  clear(): Promise<void> {
    // Só o servidor apaga o cookie, no retorno do logout ou quando encerra a
    // sessão por reuso detectado.
    return Promise.resolve();
  }
}
