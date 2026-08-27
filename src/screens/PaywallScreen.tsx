import React, { useState } from 'react';
import { Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { Button } from '../components/Button';
import { Screen } from '../components/Screen';
import { PREMIUM_BENEFITS, PRODUCTS } from '../monetization/products';
import { useApp } from '../store/AppContext';
import { colors, radius, spacing, typography } from '../theme';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'Paywall'>;
type Route = RouteProp<RootStackParamList, 'Paywall'>;

const HEADLINE: Record<string, string> = {
  'limite-baralhos': 'Crie quantos baralhos quiser',
  'limite-cards': 'Cards ilimitados em cada baralho',
  audio: 'Áudio nos dois lados do card',
  resumo: 'Estude sem interrupções',
  perfil: 'LinguaCards Premium',
};

export function PaywallScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { premium, entitlements, buyPremium, restorePremium } = useApp();

  const [selected, setSelected] = useState(
    PRODUCTS.find((product) => product.highlight)?.id ?? PRODUCTS[0].id
  );
  const [busy, setBusy] = useState(false);

  const headline = HEADLINE[params?.source ?? 'perfil'] ?? HEADLINE.perfil;

  const buy = async () => {
    setBusy(true);
    try {
      await buyPremium(selected);
      Alert.alert('Tudo certo!', 'O premium está ativo. Bons estudos.', [
        { text: 'Continuar', onPress: () => navigation.goBack() },
      ]);
    } catch {
      Alert.alert('Compra não concluída', 'Nada foi cobrado. Tente novamente mais tarde.');
    } finally {
      setBusy(false);
    }
  };

  const doRestore = async () => {
    setBusy(true);
    try {
      const restored = await restorePremium();
      Alert.alert(
        restored ? 'Assinatura restaurada' : 'Nada para restaurar',
        restored
          ? 'Seu acesso premium está ativo novamente.'
          : 'Não encontramos uma compra anterior nesta conta.'
      );
    } finally {
      setBusy(false);
    }
  };

  if (premium) {
    return (
      <Screen edges={['bottom']}>
        <ScrollView contentContainerStyle={styles.content}>
          <View style={styles.activeCard}>
            <Ionicons name="sparkles" size={30} color={colors.premium} />
            <Text style={styles.activeTitle}>Premium ativo</Text>
            <Text style={styles.activeText}>
              {entitlements.expiresAt
                ? `Sua assinatura vale até ${new Date(entitlements.expiresAt).toLocaleDateString('pt-BR')}.`
                : 'Você tem acesso vitalício a todos os recursos.'}
            </Text>
          </View>
          <Button label="Voltar" onPress={() => navigation.goBack()} size="lg" fullWidth />
        </ScrollView>
      </Screen>
    );
  }

  return (
    <Screen edges={['bottom']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.hero}>
          <View style={styles.heroIcon}>
            <Ionicons name="sparkles" size={30} color={colors.premium} />
          </View>
          <Text style={styles.heroTitle}>{headline}</Text>
          <Text style={styles.heroSubtitle}>
            Um plano só, todos os recursos liberados. Cancele quando quiser.
          </Text>
        </View>

        <View style={styles.benefits}>
          {PREMIUM_BENEFITS.map((benefit) => (
            <View key={benefit.label} style={styles.benefit}>
              <Ionicons name={benefit.icon} size={18} color={colors.premium} />
              <Text style={styles.benefitText}>{benefit.label}</Text>
            </View>
          ))}
        </View>

        <View style={styles.plans}>
          {PRODUCTS.map((product) => {
            const isSelected = product.id === selected;
            return (
              <Pressable
                key={product.id}
                onPress={() => setSelected(product.id)}
                accessibilityRole="radio"
                accessibilityState={{ selected: isSelected }}
                style={[styles.plan, isSelected && styles.planSelected]}
              >
                <View style={styles.planBody}>
                  <View style={styles.planTitleRow}>
                    <Text style={styles.planTitle}>{product.title}</Text>
                    {product.highlight ? (
                      <View style={styles.planTag}>
                        <Text style={styles.planTagText}>MAIS ESCOLHIDO</Text>
                      </View>
                    ) : null}
                  </View>
                  {product.note ? <Text style={styles.planNote}>{product.note}</Text> : null}
                </View>
                <Text style={[styles.planPrice, isSelected && styles.planPriceSelected]}>
                  {product.price}
                </Text>
              </Pressable>
            );
          })}
        </View>

        <Button
          label="Assinar"
          onPress={() => void buy()}
          loading={busy}
          variant="premium"
          size="lg"
          fullWidth
        />

        <Pressable onPress={() => void doRestore()} disabled={busy} style={styles.restore}>
          <Text style={styles.restoreText}>Restaurar compra</Text>
        </Pressable>

        <Text style={styles.legal}>
          A assinatura renova automaticamente até ser cancelada nos ajustes da loja. Nesta versão o
          acesso é registrado apenas neste aparelho.
        </Text>
      </ScrollView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  hero: { alignItems: 'center', gap: spacing.sm, paddingTop: spacing.md },
  heroIcon: {
    width: 66,
    height: 66,
    borderRadius: 22,
    backgroundColor: colors.premiumSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  heroTitle: { ...typography.h1, fontSize: 25, textAlign: 'center' },
  heroSubtitle: { ...typography.caption, color: colors.textMuted, textAlign: 'center' },

  benefits: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.md,
  },
  benefit: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  benefitText: { ...typography.body, flex: 1 },

  plans: { gap: spacing.sm },
  plan: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 2,
    borderColor: colors.border,
    padding: spacing.lg,
  },
  planSelected: { borderColor: colors.premium, backgroundColor: colors.premiumSoft },
  planBody: { flex: 1, gap: 2 },
  planTitleRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  planTitle: { ...typography.h3, fontSize: 16 },
  planTag: {
    backgroundColor: colors.premium,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: 1,
  },
  planTagText: { fontSize: 9, fontWeight: '800', color: '#1A1500', letterSpacing: 0.4 },
  planNote: { ...typography.tiny, color: colors.textMuted },
  planPrice: { ...typography.h3, fontSize: 17, color: colors.textMuted },
  planPriceSelected: { color: colors.premium },

  restore: { alignItems: 'center', paddingVertical: spacing.sm },
  restoreText: { ...typography.caption, color: colors.textMuted, textDecorationLine: 'underline' },
  legal: { ...typography.tiny, color: colors.textFaint, textAlign: 'center', lineHeight: 16 },

  activeCard: {
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.lg,
    padding: spacing.xl,
    marginTop: spacing.xl,
  },
  activeTitle: { ...typography.h2 },
  activeText: { ...typography.caption, color: colors.textMuted, textAlign: 'center' },
});
