import { Stack } from 'expo-router';

/**
 * Pilha da aba Membros.
 *
 * A barra de abas continua visível ao empilhar a ficha — comportamento nativo
 * de tab bar, e o usuário não perde o "onde estou" ao entrar em um cadastro.
 * "Novo membro" e "Editar membro" são modais: são tarefas que se completam ou
 * se cancelam, não lugares que se visitam, e o gesto de puxar para fechar
 * deixa isso explícito.
 */
export default function MembrosLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
      <Stack.Screen name="[id]" />
      <Stack.Screen name="novo" options={{ presentation: 'modal', animation: 'slide_from_bottom' }} />
      <Stack.Screen
        name="editar/[id]"
        options={{ presentation: 'modal', animation: 'slide_from_bottom' }}
      />
      <Stack.Screen name="familias/index" />
      <Stack.Screen name="familias/[id]" />
      <Stack.Screen
        name="familias/nova"
        options={{ presentation: 'modal', animation: 'slide_from_bottom' }}
      />
      <Stack.Screen name="importar" options={{ presentation: 'modal', animation: 'slide_from_bottom' }} />
    </Stack>
  );
}
