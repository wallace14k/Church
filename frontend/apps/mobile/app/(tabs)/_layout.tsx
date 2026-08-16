import { Feather } from '@expo/vector-icons';
import { useTheme } from '@congrega/ui/theme';
import { Tabs } from 'expo-router';
import { Platform } from 'react-native';

/**
 * Navegação principal, pós-login.
 *
 * Só duas abas hoje — Início e Membros — porque só duas áreas existem de
 * verdade. Uma barra com abas desabilitadas ou "em breve" seria pior que uma
 * barra pequena: prometeria navegação que ainda não existe. Financeiro,
 * calendário e Congrega+ ganham aba no dia em que tiverem tela.
 */
export default function TabsLayout() {
  const theme = useTheme();

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: theme.colors.brand,
        tabBarInactiveTintColor: theme.colors.textMuted,
        tabBarStyle: {
          backgroundColor: theme.colors.surface,
          borderTopColor: theme.colors.hairline,
          borderTopWidth: 1,
          height: Platform.OS === 'ios' ? 88 : 64,
          paddingTop: 8,
          paddingBottom: Platform.OS === 'ios' ? 30 : 10,
          // Sem sombra: a linha de 1px já separa a barra do conteúdo, na mesma
          // linguagem de papel-sobre-papel do resto do sistema.
          elevation: 0,
          shadowOpacity: 0,
        },
        tabBarLabelStyle: {
          fontFamily: theme.type.eyebrow.fontFamily,
          fontSize: 10,
          letterSpacing: 0.6,
          marginTop: 2,
        },
      }}
    >
      <Tabs.Screen
        name="inicio"
        options={{
          title: 'Início',
          tabBarIcon: ({ color, size }) => <Feather name="home" size={size - 3} color={color} />,
        }}
      />
      <Tabs.Screen
        name="membros"
        options={{
          title: 'Membros',
          tabBarIcon: ({ color, size }) => <Feather name="users" size={size - 3} color={color} />,
        }}
      />
    </Tabs>
  );
}
