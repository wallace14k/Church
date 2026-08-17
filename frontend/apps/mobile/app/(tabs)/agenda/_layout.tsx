import { Stack } from 'expo-router';

/** Pilha da aba Agenda. Cadastro e edição são modais; a ficha do evento empilha. */
export default function AgendaLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
      <Stack.Screen name="[id]" />
      <Stack.Screen name="novo" options={{ presentation: 'modal', animation: 'slide_from_bottom' }} />
      <Stack.Screen
        name="editar/[id]"
        options={{ presentation: 'modal', animation: 'slide_from_bottom' }}
      />
    </Stack>
  );
}
