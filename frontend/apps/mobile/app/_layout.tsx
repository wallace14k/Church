import { ThemeProvider } from '@congrega/ui/theme';
import {
  Inter_400Regular,
  Inter_500Medium,
  Inter_600SemiBold,
  Inter_700Bold,
  Inter_800ExtraBold,
  useFonts as useInter,
} from '@expo-google-fonts/inter';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { ActivityIndicator, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { GlobalLoadingProvider } from '@congrega/ui/GlobalLoading';
import { SessionProvider } from '../src/session';

export default function RootLayout() {
  // As chaves precisam bater com os nomes em @congrega/ui/tokens. Divergir faz o
  // React Native cair na fonte do sistema em silêncio — sem erro no console, e a
  // identidade visual simplesmente não aparece.
  //
  // Uma família só (Inter), do corpo ao título, em DOIS pesos: a §3 do design
  // system admite 400 e 500 e proíbe 600 e 700. O peso 600 foi removido daqui
  // junto com os tokens — deixá-lo carregado só convidaria alguém a usá-lo, e
  // `tokens.test.ts` falha se ele reaparecer na escala.
  // Cinco pesos, e não dois. A hierarquia do sistema Grove é feita por PESO —
  // o nome da marca e o rótulo de seção têm quase o mesmo corpo e se distinguem
  // por 800 contra 400. Sem carregar aqui, tudo cai no peso mais próximo e a
  // hierarquia some sem nenhum erro aparecer. Ver  em tokens.ts.
  const [fontesProntas] = useInter({
    Inter_400Regular,
    Inter_500Medium,
    Inter_600SemiBold,
    Inter_700Bold,
    Inter_800ExtraBold,
  });

  return (
    <SafeAreaProvider>
      <ThemeProvider>
        {/* Canvas branco: a barra de status precisa de conteúdo escuro. */}
        <StatusBar style="dark" />
        {fontesProntas ? (
          <SessionProvider>
            {/* O carregamento global cobre a aplicação inteira: ele bloqueia a
                tela durante uma gravação, e uma gravação pode ser disparada de
                qualquer módulo. Abaixo do SessionProvider porque nada dele
                depende de sessão — e acima do Stack para sobrepor toda rota. */}
            <GlobalLoadingProvider>
            {/* Cada tela desenha o próprio cabeçalho — o header padrão do Stack
                não conhece a tipografia do sistema e quebraria a identidade. */}
            <Stack screenOptions={{ headerShown: false, animation: 'fade' }} />
            </GlobalLoadingProvider>
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
            {/* Literais, e não tokens: este ramo existe justamente enquanto o
                app ainda está montando, antes do provider de tema. Os valores
                são `palette.pureWhite` e `palette.offBlackInk`. */}
            <ActivityIndicator color="#14140F" />
          </View>
        )}
      </ThemeProvider>
    </SafeAreaProvider>
  );
}
