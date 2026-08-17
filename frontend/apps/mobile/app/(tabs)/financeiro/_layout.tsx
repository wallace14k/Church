import { Stack } from 'expo-router';

/**
 * Pilha da aba Financeiro.
 *
 * "Lançar" é modal — é uma tarefa que se completa ou se cancela, não um lugar
 * que se visita, e o gesto de puxar para fechar diz isso sem texto. Fechamento
 * e categorias são lugares: empilham e mantêm a navegação principal visível.
 */
export default function FinanceiroLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
      <Stack.Screen name="fechamento" />
      <Stack.Screen name="categorias" />
      <Stack.Screen name="lancar" options={{ presentation: 'modal', animation: 'slide_from_bottom' }} />
    </Stack>
  );
}
