import React from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { NavigationContainer, DarkTheme, type Theme } from '@react-navigation/native';
import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

import { useAuth } from '../auth/AuthContext';
import { CardFormScreen } from '../screens/CardFormScreen';
import { DeckDetailScreen } from '../screens/DeckDetailScreen';
import { DeckFormScreen } from '../screens/DeckFormScreen';
import { DecksScreen } from '../screens/DecksScreen';
import { LoginScreen } from '../screens/LoginScreen';
import { PaywallScreen } from '../screens/PaywallScreen';
import { ProfileScreen } from '../screens/ProfileScreen';
import { SessionSummaryScreen } from '../screens/SessionSummaryScreen';
import { StatsScreen } from '../screens/StatsScreen';
import { StudyScreen } from '../screens/StudyScreen';
import { useApp } from '../store/AppContext';
import { colors } from '../theme';
import type { MainTabParamList, RootStackParamList } from './types';

const Stack = createNativeStackNavigator<RootStackParamList>();
const Tabs = createBottomTabNavigator<MainTabParamList>();

const navigationTheme: Theme = {
  ...DarkTheme,
  colors: {
    ...DarkTheme.colors,
    background: colors.bg,
    card: colors.bg,
    text: colors.text,
    border: colors.border,
    primary: colors.primary,
    notification: colors.forgot,
  },
};

const TAB_ICONS: Record<keyof MainTabParamList, keyof typeof Ionicons.glyphMap> = {
  Decks: 'albums',
  Stats: 'stats-chart',
  Profile: 'person',
};

function MainTabs() {
  return (
    <Tabs.Navigator
      screenOptions={({ route }) => ({
        headerShown: false,
        tabBarActiveTintColor: colors.primary,
        tabBarInactiveTintColor: colors.textFaint,
        tabBarStyle: { backgroundColor: colors.bgElevated, borderTopColor: colors.border },
        tabBarIcon: ({ color, size, focused }) => (
          <Ionicons
            name={focused ? TAB_ICONS[route.name] : (`${TAB_ICONS[route.name]}-outline` as keyof typeof Ionicons.glyphMap)}
            size={size}
            color={color}
          />
        ),
      })}
    >
      <Tabs.Screen name="Decks" component={DecksScreen} options={{ title: 'Baralhos' }} />
      <Tabs.Screen name="Stats" component={StatsScreen} options={{ title: 'Progresso' }} />
      <Tabs.Screen name="Profile" component={ProfileScreen} options={{ title: 'Perfil' }} />
    </Tabs.Navigator>
  );
}

export function RootNavigator() {
  const { user, loading } = useAuth();
  const { ready } = useApp();

  if (loading || !ready) {
    return (
      <View style={styles.splash}>
        <ActivityIndicator color={colors.primary} size="large" />
      </View>
    );
  }

  return (
    <NavigationContainer theme={navigationTheme}>
      {user ? (
        <Stack.Navigator
          screenOptions={{
            headerStyle: { backgroundColor: colors.bg },
            headerTintColor: colors.text,
            headerTitleStyle: { fontWeight: '600' },
            headerShadowVisible: false,
            contentStyle: { backgroundColor: colors.bg },
          }}
        >
          <Stack.Screen name="Main" component={MainTabs} options={{ headerShown: false }} />
          <Stack.Screen
            name="DeckForm"
            component={DeckFormScreen}
            options={({ route }) => ({
              title: route.params?.deckId ? 'Editar baralho' : 'Novo baralho',
              presentation: 'modal',
            })}
          />
          <Stack.Screen name="DeckDetail" component={DeckDetailScreen} options={{ title: 'Baralho' }} />
          <Stack.Screen
            name="CardForm"
            component={CardFormScreen}
            options={{ title: 'Card', presentation: 'modal' }}
          />
          <Stack.Screen
            name="Study"
            component={StudyScreen}
            options={{ title: 'Treino', gestureEnabled: false }}
          />
          <Stack.Screen
            name="SessionSummary"
            component={SessionSummaryScreen}
            options={{ headerShown: false, gestureEnabled: false }}
          />
          <Stack.Screen
            name="Paywall"
            component={PaywallScreen}
            options={{ title: 'Premium', presentation: 'modal' }}
          />
        </Stack.Navigator>
      ) : (
        <LoginScreen />
      )}
    </NavigationContainer>
  );
}

const styles = StyleSheet.create({
  splash: { flex: 1, backgroundColor: colors.bg, alignItems: 'center', justifyContent: 'center' },
});
