import React, { useEffect, useState } from 'react';
import { Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { AD_SKIP_AFTER_SECONDS, isAdNetworkConfigured } from '../monetization/ads';
import { colors, radius, spacing, typography } from '../theme';

interface Props {
  visible: boolean;
  onClose: () => void;
  /** Leva o usuário para a tela de assinatura. */
  onGoPremium: () => void;
}

/**
 * Anúncio intersticial exibido ao fim de um treino.
 *
 * Enquanto o AdMob não está configurado (`ads.ts`), este componente ocupa o
 * lugar dele com o mesmo comportamento: tela cheia, contagem regressiva antes
 * de liberar o fechamento e um atalho para remover os anúncios assinando.
 */
export function InterstitialAd({ visible, onClose, onGoPremium }: Props) {
  const [secondsLeft, setSecondsLeft] = useState(AD_SKIP_AFTER_SECONDS);

  useEffect(() => {
    if (!visible) {
      setSecondsLeft(AD_SKIP_AFTER_SECONDS);
      return;
    }
    const timer = setInterval(() => {
      setSecondsLeft((current) => (current <= 1 ? 0 : current - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, [visible]);

  const canClose = secondsLeft === 0;

  return (
    <Modal visible={visible} animationType="fade" transparent statusBarTranslucent>
      <View style={styles.backdrop}>
        <View style={styles.sheet}>
          <View style={styles.topRow}>
            <Text style={styles.tag}>Anúncio</Text>
            <Pressable
              onPress={canClose ? onClose : undefined}
              disabled={!canClose}
              hitSlop={10}
              accessibilityRole="button"
              accessibilityLabel={canClose ? 'Fechar anúncio' : `Aguarde ${secondsLeft} segundos`}
            >
              {canClose ? (
                <Ionicons name="close" size={22} color={colors.textMuted} />
              ) : (
                <Text style={styles.countdown}>{secondsLeft}</Text>
              )}
            </Pressable>
          </View>

          <View style={styles.slot}>
            <Ionicons name="megaphone-outline" size={38} color={colors.textFaint} />
            <Text style={styles.slotTitle}>
              {isAdNetworkConfigured ? 'Carregando anúncio…' : 'Espaço reservado para anúncio'}
            </Text>
            <Text style={styles.slotText}>
              {isAdNetworkConfigured
                ? 'O anúncio aparece aqui assim que a rede responde.'
                : 'Preencha os IDs do AdMob em app.json para exibir anúncios reais nesta build.'}
            </Text>
          </View>

          <Pressable onPress={onGoPremium} style={styles.upsell}>
            <Ionicons name="sparkles-outline" size={16} color={colors.premium} />
            <Text style={styles.upsellText}>Estudar sem anúncios</Text>
            <Ionicons name="chevron-forward" size={16} color={colors.premium} />
          </Pressable>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: colors.overlay,
    alignItems: 'center',
    justifyContent: 'center',
    padding: spacing.lg,
  },
  sheet: {
    width: '100%',
    maxWidth: 420,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.xl,
    padding: spacing.lg,
    gap: spacing.lg,
  },
  topRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  tag: {
    ...typography.tiny,
    color: colors.textFaint,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  countdown: {
    ...typography.caption,
    color: colors.textMuted,
    width: 24,
    textAlign: 'center',
  },
  slot: {
    minHeight: 220,
    borderRadius: radius.lg,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    padding: spacing.lg,
  },
  slotTitle: { ...typography.h3, textAlign: 'center' },
  slotText: { ...typography.caption, color: colors.textFaint, textAlign: 'center', lineHeight: 18 },
  upsell: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.md,
    paddingVertical: spacing.md,
  },
  upsellText: { ...typography.caption, color: colors.premium, fontWeight: '700' },
});
