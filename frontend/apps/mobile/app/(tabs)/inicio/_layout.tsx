import { Stack } from 'expo-router';

/**
 * Pilha da aba Início.
 *
 * Mesmo raciocínio da pilha de Membros: a barra de abas (ou a sidebar, no
 * web) continua visível ao empilhar Aniversariantes, e o usuário não perde o
 * "onde estou" ao ver a lista completa a partir do painel.
 */
export default function InicioLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
      <Stack.Screen name="aniversariantes" />
    </Stack>
  );
}
