import { Feather } from '@expo/vector-icons';
import { useTheme } from '@congrega/ui/theme';
import { Tabs } from 'expo-router';
import { Platform } from 'react-native';

/**
 * Navegação principal, pós-login.
 *
 * Uma aba por área que existe de verdade. Aba desabilitada ou "em breve" seria
 * pior que barra pequena: prometeria navegação que ainda não existe.
 *
 * **Congrega+ não tem gate de papel**, ao contrário de Financeiro: é a
 * assinatura da pessoa, não da igreja — aparece mesmo para quem não tem
 * vínculo com nenhuma congregação, o público que o produto B2C existe para
 * atender (ver CLAUDE.md).
 */
export default function TabsLayout() {
  const theme = useTheme();

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        // Tinta, não o lima: a aba ativa é ícone + rótulo, e lima como cor de
        // texto sobre a barra clara mede 1,2:1. O contraste entre tinta e
        // grafite é o que diz qual aba está ativa.
        tabBarActiveTintColor: theme.colors.text,
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
          fontSize: theme.type.eyebrow.fontSize,
          letterSpacing: theme.type.eyebrow.letterSpacing,
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
      <Tabs.Screen
        name="agenda"
        options={{
          title: 'Agenda',
          tabBarIcon: ({ color, size }) => <Feather name="calendar" size={size - 3} color={color} />,
        }}
      />
      <Tabs.Screen
        name="financeiro"
        options={{
          title: 'Financeiro',
          tabBarIcon: ({ color, size }) => (
            <Feather name="trending-up" size={size - 3} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="assinatura"
        options={{
          title: 'Congrega+',
          tabBarIcon: ({ color, size }) => <Feather name="award" size={size - 3} color={color} />,
        }}
      />
    </Tabs>
  );
}
