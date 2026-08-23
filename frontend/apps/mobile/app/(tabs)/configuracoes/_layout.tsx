import { Stack } from 'expo-router';

/**
 * Pilha de Configurações.
 *
 * Uma tela só por enquanto. O Stack existe para que acrescentar a próxima —
 * papéis e permissões, por exemplo — não exija reorganizar a navegação.
 */
export default function ConfiguracoesLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
    </Stack>
  );
}
