import { Stack } from 'expo-router';

/** Pilha da aba Congrega+. Uma tela só por enquanto — status ou vitrine de planos. */
export default function AssinaturaLayout() {
  return (
    <Stack screenOptions={{ headerShown: false, animation: 'slide_from_right' }}>
      <Stack.Screen name="index" />
    </Stack>
  );
}
