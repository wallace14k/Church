import { ThemeProvider } from '@congrega/ui/theme';
import {
  Inter_400Regular,
  Inter_500Medium,
  Inter_600SemiBold,
  useFonts as useInter,
} from '@expo-google-fonts/inter';
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
  // Uma família só (Inter), do corpo ao título — o padrão de referência não usa
  // serifada em nenhum momento.
  const [fontesProntas] = useInter({ Inter_400Regular, Inter_500Medium, Inter_600SemiBold });

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
            <ActivityIndicator color="#171923" />
          </View>
        )}
      </ThemeProvider>
    </SafeAreaProvider>
  );
}
