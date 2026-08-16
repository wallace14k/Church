import * as SecureStore from 'expo-secure-store';
import { REFRESH_TOKEN_KEY, type TokenStorage } from './token-storage';

/**
 * Guarda o refresh token no Keychain (iOS) ou Keystore (Android).
 *
 * O Metro escolhe este arquivo em iOS e Android pela extensão `.native.ts`.
 * Nenhum outro ponto do código sabe que `expo-secure-store` existe.
 *
 * `WHEN_UNLOCKED_THIS_DEVICE_ONLY` é deliberado e vale explicar:
 * - `WHEN_UNLOCKED` impede leitura com o aparelho bloqueado, que é justamente a
 *   situação de um aparelho perdido ou apreendido.
 * - `THIS_DEVICE_ONLY` mantém o item fora do backup do iCloud. Sem isso, o
 *   refresh token viaja para o backup e um restore em outro aparelho traria a
 *   sessão junto — exatamente o que a rotação de token existe para impedir.
 */
export class SecureTokenStorage implements TokenStorage {
  async read(): Promise<string | null> {
    try {
      return await SecureStore.getItemAsync(REFRESH_TOKEN_KEY, {
        keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
      });
    } catch {
      // Keychain indisponível (aparelho bloqueado durante uma tarefa em
      // background, item corrompido por restore). Tratar como "sem sessão" é
      // seguro: o pior resultado é pedir um login a mais. Propagar a exceção
      // travaria o app na tela de abertura.
      return null;
    }
  }

  async write(token: string): Promise<void> {
    await SecureStore.setItemAsync(REFRESH_TOKEN_KEY, token, {
      keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
    });
  }

  async clear(): Promise<void> {
    try {
      await SecureStore.deleteItemAsync(REFRESH_TOKEN_KEY);
    } catch {
      // Apagar o que já não existe não é erro. Falhar aqui impediria o logout
      // de concluir, deixando o usuário preso numa sessão que ele pediu para
      // encerrar.
    }
  }
}
