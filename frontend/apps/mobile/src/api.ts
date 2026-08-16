import { ApiClient } from '@congrega/api-client/client';
import type { TokenStorage } from '@congrega/api-client/token-storage';
import Constants from 'expo-constants';
import { Platform } from 'react-native';

/**
 * Instância única do cliente.
 *
 * Módulo, e não Context: o cliente não guarda estado de interface e não precisa
 * re-renderizar nada quando muda. Pô-lo em Context obrigaria toda tela que faz
 * requisição a consumir o provider, sem nenhum ganho.
 */

function resolveBaseUrl(): string {
  const configured = Constants.expoConfig?.extra?.['apiBaseUrl'];

  if (typeof configured === 'string' && configured.length > 0) {
    return configured;
  }

  // Falha alto em vez de cair para localhost em silêncio. Um build de produção
  // apontando para a máquina do desenvolvedor é o tipo de erro que só aparece
  // depois da publicação na loja.
  throw new Error(
    'apiBaseUrl não configurado. Defina extra.apiBaseUrl em app.json ou EXPO_PUBLIC_API_URL.',
  );
}

/**
 * Escolhe o storage pela plataforma.
 *
 * O `require` é intencional: `import` estático de `token-storage.native` traria
 * o `expo-secure-store` para o bundle web, onde ele não existe. Aqui o módulo só
 * é resolvido no ramo que de fato roda.
 */
function createStorage(): TokenStorage {
  if (Platform.OS === 'web') {
    const { CookieTokenStorage } = require('@congrega/api-client/token-storage.web');
    return new CookieTokenStorage();
  }

  const { SecureTokenStorage } = require('@congrega/api-client/token-storage.native');
  return new SecureTokenStorage();
}

let onSessionEndedHandler: (() => void) | null = null;

/**
 * Registra quem deve ser avisado quando a sessão cair de vez.
 *
 * Indireção necessária porque o cliente nasce antes da árvore React: sem ela, o
 * módulo precisaria conhecer o roteador, e um módulo de rede que sabe navegar é
 * um acoplamento que cobra caro na primeira mudança de navegação.
 */
export function setSessionEndedHandler(handler: (() => void) | null): void {
  onSessionEndedHandler = handler;
}

export const apiClient = new ApiClient({
  baseUrl: resolveBaseUrl(),
  storage: createStorage(),
  clientKind: Platform.OS === 'web' ? 'web' : 'mobile',
  onSessionEnded: () => onSessionEndedHandler?.(),
});
