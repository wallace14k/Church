import {
  BricolageGrotesque_600SemiBold,
  useFonts as useBricolage,
} from '@expo-google-fonts/bricolage-grotesque';
import { IBMPlexMono_700Bold, useFonts as usePlexMono } from '@expo-google-fonts/ibm-plex-mono';
import {
  IBMPlexSans_400Regular,
  IBMPlexSans_500Medium,
  IBMPlexSans_700Bold,
  useFonts as usePlexSans,
} from '@expo-google-fonts/ibm-plex-sans';
import { ThemeProvider } from '@congrega/ui/theme';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { ActivityIndicator, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { SessionProvider } from '../src/session';

export default function RootLayout() {
  // As famílias são carregadas com os nomes exatos dos tokens em @congrega/ui.
  // Divergir aqui faz o RN cair silenciosamente na fonte do sistema, e a
  // identidade visual some sem nenhum erro no console.
  const [bricolageOk] = useBricolage({ BricolageGrotesque: BricolageGrotesque_600SemiBold });
  const [plexSansOk] = usePlexSans({
    IBMPlexSans: IBMPlexSans_400Regular,
    IBMPlexSans_Medium: IBMPlexSans_500Medium,
    IBMPlexSans_Bold: IBMPlexSans_700Bold,
  });
  const [plexMonoOk] = usePlexMono({ IBMPlexMono: IBMPlexMono_700Bold, IBMPlexMono_Bold: IBMPlexMono_700Bold });

  const fontesProntas = bricolageOk && plexSansOk && plexMonoOk;

  return (
    <SafeAreaProvider>
      <ThemeProvider>
        <StatusBar style="auto" />
        {fontesProntas ? (
          <SessionProvider>
            {/* headerShown: false — cada tela desenha o próprio cabeçalho.
                O header padrão do Stack não conhece a tipografia do sistema de
                design e quebraria a identidade logo na primeira tela. */}
            <Stack screenOptions={{ headerShown: false, animation: 'fade' }} />
          </SessionProvider>
        ) : (
          <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
            <ActivityIndicator />
          </View>
        )}
      </ThemeProvider>
    </SafeAreaProvider>
  );
}
