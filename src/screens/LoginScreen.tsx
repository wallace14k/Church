import React, { useState } from 'react';
import { Alert, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as AppleAuthentication from 'expo-apple-authentication';

import { Screen } from '../components/Screen';
import { AuthError, useAuth } from '../auth/AuthContext';
import { colors, radius, spacing, typography } from '../theme';

/** Tela de entrada: Google, conta Apple e um modo local sem conta. */
export function LoginScreen() {
  const { signInWithGoogle, signInWithApple, continueAsGuest, pending, appleAvailable } = useAuth();
  const [error, setError] = useState<string | null>(null);

  const run = async (action: () => Promise<void>) => {
    setError(null);
    try {
      await action();
    } catch (caught) {
      const message =
        caught instanceof AuthError
          ? caught.message
          : 'Algo deu errado ao entrar. Tente novamente.';
      setError(message);
    }
  };

  return (
    <Screen edges={['top', 'bottom']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.hero}>
          <View style={styles.logo}>
            <Ionicons name="layers" size={34} color={colors.primary} />
          </View>
          <Text style={styles.title}>LinguaCards</Text>
          <Text style={styles.subtitle}>
            Flashcards com repetição espaçada e áudio. Você cria os baralhos, grava os diálogos e o
            app cuida de quando cada card volta.
          </Text>
        </View>

        <View style={styles.highlights}>
          <Highlight icon="mic-outline" text="Grave o diálogo direto no card" />
          <Highlight icon="repeat-outline" text="Três respostas definem quando o card volta" />
          <Highlight icon="flame-outline" text="Ofensiva diária para manter o ritmo" />
        </View>

        <View style={styles.actions}>
          {Platform.OS === 'ios' && appleAvailable ? (
            <AppleAuthentication.AppleAuthenticationButton
              buttonType={AppleAuthentication.AppleAuthenticationButtonType.SIGN_IN}
              buttonStyle={AppleAuthentication.AppleAuthenticationButtonStyle.WHITE}
              cornerRadius={radius.md}
              style={styles.appleButton}
              onPress={() => void run(signInWithApple)}
            />
          ) : null}

          <Pressable
            onPress={() => void run(signInWithGoogle)}
            disabled={pending !== null}
            style={({ pressed }) => [
              styles.provider,
              pressed && styles.pressed,
              pending !== null && styles.disabled,
            ]}
          >
            <Ionicons name="logo-google" size={19} color="#1A1A1A" />
            <Text style={styles.providerLabel}>
              {pending === 'google' ? 'Conectando…' : 'Continuar com Google'}
            </Text>
          </Pressable>

          <Pressable
            onPress={() =>
              Alert.alert(
                'Usar sem conta',
                'Seus baralhos ficam só neste aparelho e não são recuperados se o app for desinstalado. Você pode entrar com uma conta depois.',
                [
                  { text: 'Voltar', style: 'cancel' },
                  { text: 'Continuar', onPress: () => void run(continueAsGuest) },
                ]
              )
            }
            disabled={pending !== null}
            style={({ pressed }) => [styles.guest, pressed && styles.pressed]}
          >
            <Text style={styles.guestLabel}>Usar sem conta neste aparelho</Text>
          </Pressable>
        </View>

        {error ? (
          <View style={styles.error}>
            <Ionicons name="alert-circle-outline" size={16} color={colors.forgot} />
            <Text style={styles.errorText}>{error}</Text>
          </View>
        ) : null}

        <Text style={styles.legal}>
          Nesta versão, baralhos, cards e áudios ficam salvos apenas no seu aparelho.
        </Text>
      </ScrollView>
    </Screen>
  );
}

function Highlight({ icon, text }: { icon: keyof typeof Ionicons.glyphMap; text: string }) {
  return (
    <View style={styles.highlight}>
      <Ionicons name={icon} size={18} color={colors.primary} />
      <Text style={styles.highlightText}>{text}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { flexGrow: 1, padding: spacing.xl, justifyContent: 'center', gap: spacing.xxl },
  hero: { alignItems: 'center', gap: spacing.md },
  logo: {
    width: 76,
    height: 76,
    borderRadius: 24,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: { ...typography.h1, fontSize: 32 },
  subtitle: { ...typography.bodyMuted, textAlign: 'center', lineHeight: 22 },
  highlights: { gap: spacing.md },
  highlight: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  highlightText: { ...typography.body, flex: 1 },
  actions: { gap: spacing.md },
  appleButton: { height: 52, width: '100%' },
  provider: {
    height: 52,
    borderRadius: radius.md,
    backgroundColor: '#FFFFFF',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.md,
  },
  providerLabel: { fontSize: 16, fontWeight: '600', color: '#1A1A1A' },
  pressed: { opacity: 0.8 },
  disabled: { opacity: 0.6 },
  guest: { alignItems: 'center', paddingVertical: spacing.sm },
  guestLabel: { ...typography.caption, color: colors.textMuted, textDecorationLine: 'underline' },
  error: {
    flexDirection: 'row',
    gap: spacing.sm,
    alignItems: 'flex-start',
    backgroundColor: colors.forgotSoft,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  errorText: { ...typography.caption, color: colors.forgot, flex: 1, lineHeight: 18 },
  legal: { ...typography.tiny, color: colors.textFaint, textAlign: 'center', lineHeight: 16 },
});
