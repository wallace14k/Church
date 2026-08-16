import { ThemeProvider } from '@congrega/ui/theme';
import {
  Manrope_400Regular,
  Manrope_500Medium,
  Manrope_600SemiBold,
  Manrope_700Bold,
  useFonts as useManrope,
} from '@expo-google-fonts/manrope';
import {
  PlusJakartaSans_500Medium,
  PlusJakartaSans_600SemiBold,
  useFonts as useJakarta,
} from '@expo-google-fonts/plus-jakarta-sans';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { ActivityIndicator, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SessionProvider } from '../src/session';

export default function RootLayout() {
  // As chaves precisam bater com os nomes em @congrega/ui/tokens. Divergir faz o
  // React Native cair na fonte do sistema em silêncio — sem erro no console, e a
  // identidade visual simplesmente não aparece.
  //
  // Manrope substitui Switzer, Plus Jakarta Sans substitui Basier Circle. Ambos
  // os substitutos são autorizados pelo DESIGN.md; os originais são comerciais.
  const [manropeOk] = useManrope({
    Manrope_400Regular,
    Manrope_500Medium,
    Manrope_600SemiBold,
    Manrope_700Bold,
  });
  const [jakartaOk] = useJakarta({ PlusJakartaSans_500Medium, PlusJakartaSans_600SemiBold });

  const fontesProntas = manropeOk && jakartaOk;

  return (
    <SafeAreaProvider>
      <ThemeProvider>
        {/* Canvas branco: a barra de status precisa de conteúdo escuro. */}
        <StatusBar style="dark" />
        {fontesProntas ? (
          <SessionProvider>
            {/* Cada tela desenha o próprio cabeçalho — o header padrão do Stack
                não conhece a tipografia do sistema e quebraria a identidade. */}
            <Stack screenOptions={{ headerShown: false, animation: 'fade' }} />
          </SessionProvider>
        ) : (
          <View
            style={{
              flex: 1,
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: '#FFFFFF',
            }}
          >
            <ActivityIndicator color="#08304C" />
          </View>
        )}
      </ThemeProvider>
    </SafeAreaProvider>
  );
}
