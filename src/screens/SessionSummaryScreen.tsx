import React, { useEffect, useMemo, useState } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation, useRoute, type RouteProp } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';

import { Button } from '../components/Button';
import { InterstitialAd } from '../components/InterstitialAd';
import { ProgressBar } from '../components/ProgressBar';
import { Screen } from '../components/Screen';
import { shouldShowAd, shouldShowPaywall } from '../monetization/ads';
import { useApp } from '../store/AppContext';
import { goalReachedToday, reviewsToday, MAX_FREEZES } from '../streak';
import { colors, radius, spacing, typography } from '../theme';
import { formatDuration } from '../utils/date';
import type { RootStackParamList } from '../navigation/types';

type Nav = NativeStackNavigationProp<RootStackParamList, 'SessionSummary'>;
type Route = RouteProp<RootStackParamList, 'SessionSummary'>;

/**
 * Resumo do treino.
 *
 * É o ponto de monetização do app: o anúncio intersticial aparece aqui, ao
 * fim do treino (nunca no meio dele), e é também daqui que sai o convite para
 * assinar. Quem prefere não pagar pode assistir a um anúncio e ganhar um
 * protetor de ofensiva.
 */
export function SessionSummaryScreen() {
  const navigation = useNavigation<Nav>();
  const { params } = useRoute<Route>();
  const { result } = params;

  const { streak, sessionCount, premium, getDeckStats, addFreeze } = useApp();
  const [adVisible, setAdVisible] = useState(false);
  const [rewardClaimed, setRewardClaimed] = useState(false);
  const [rewardPending, setRewardPending] = useState(false);

  // O anúncio de fim de treino abre assim que o resumo aparece.
  useEffect(() => {
    if (shouldShowAd(sessionCount, premium)) setAdVisible(true);
  }, [sessionCount, premium]);

  const accuracy = result.total > 0 ? (result.known + result.partial * 0.5) / result.total : 0;
  const durationMs = result.finishedAt - result.startedAt;
  const remaining = getDeckStats(result.deckId).readyNow;

  const today = reviewsToday(streak);
  const goalDone = goalReachedToday(streak);
  const offerPaywall = useMemo(
    () => shouldShowPaywall(sessionCount, premium),
    [sessionCount, premium]
  );

  const canEarnFreeze = !premium && !rewardClaimed && streak.freezes < MAX_FREEZES;

  const claimFreeze = () => {
    setRewardPending(true);
    setAdVisible(true);
  };

  const handleAdClosed = async () => {
    setAdVisible(false);
    if (rewardPending) {
      await addFreeze(1);
      setRewardClaimed(true);
      setRewardPending(false);
    }
  };

  const goHome = () => navigation.navigate('Main');

  return (
    <Screen edges={['top', 'bottom']}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.hero}>
          <View style={[styles.heroIcon, result.goalReached && styles.heroIconGoal]}>
            <Ionicons
              name={result.goalReached ? 'flame' : 'checkmark-done'}
              size={34}
              color={result.goalReached ? colors.streak : colors.known}
            />
          </View>
          <Text style={styles.heroTitle}>
            {result.goalReached ? 'Meta do dia batida!' : 'Treino concluído'}
          </Text>
          <Text style={styles.heroSubtitle}>
            {result.total} {result.total === 1 ? 'resposta' : 'respostas'} em{' '}
            {formatDuration(durationMs)} · {result.deckName}
          </Text>
        </View>

        {result.goalReached ? (
          <View style={styles.streakCard}>
            <Ionicons name="flame" size={22} color={colors.streak} />
            <View style={styles.streakBody}>
              <Text style={styles.streakTitle}>
                Ofensiva de {result.streakAfter} {result.streakAfter === 1 ? 'dia' : 'dias'}
              </Text>
              <Text style={styles.streakText}>
                {result.streakAfter > result.streakBefore
                  ? 'Volte amanhã para não perder o ritmo.'
                  : 'Você já tinha batido a meta hoje.'}
              </Text>
            </View>
          </View>
        ) : (
          <View style={styles.goalCard}>
            <View style={styles.goalRow}>
              <Text style={styles.goalLabel}>Meta de hoje</Text>
              <Text style={styles.goalValue}>
                {Math.min(today, streak.dailyGoal)}/{streak.dailyGoal}
              </Text>
            </View>
            <ProgressBar
              progress={today / streak.dailyGoal}
              color={goalDone ? colors.known : colors.streak}
            />
            <Text style={styles.goalHint}>
              Faltam {Math.max(0, streak.dailyGoal - today)} cards para manter a ofensiva viva.
            </Text>
          </View>
        )}

        <View style={styles.breakdown}>
          <Text style={styles.sectionTitle}>Como você foi</Text>
          <Row label="Lembro" value={result.known} total={result.total} color={colors.known} />
          <Row label="Mais ou menos" value={result.partial} total={result.total} color={colors.partial} />
          <Row label="Não lembro" value={result.forgot} total={result.total} color={colors.forgot} />

          <View style={styles.summaryRow}>
            <Metric label="Acerto" value={`${Math.round(accuracy * 100)}%`} />
            <Metric label="Cards novos" value={String(result.newCards)} />
            <Metric
              label="Tempo médio"
              value={result.total > 0 ? `${Math.round(durationMs / result.total / 1000)}s` : '—'}
            />
          </View>
        </View>

        {canEarnFreeze ? (
          <Pressable onPress={claimFreeze} style={styles.rewardCard}>
            <Ionicons name="snow-outline" size={20} color={colors.primary} />
            <View style={styles.rewardBody}>
              <Text style={styles.rewardTitle}>Ganhe um protetor de ofensiva</Text>
              <Text style={styles.rewardText}>
                Assista a um anúncio curto e garanta um dia de folga sem perder a ofensiva.
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color={colors.textFaint} />
          </Pressable>
        ) : null}

        {rewardClaimed ? (
          <View style={styles.rewardDone}>
            <Ionicons name="checkmark-circle" size={18} color={colors.known} />
            <Text style={styles.rewardDoneText}>
              Protetor adicionado — você tem {streak.freezes}.
            </Text>
          </View>
        ) : null}

        {offerPaywall && !premium ? (
          <Pressable
            onPress={() => navigation.navigate('Paywall', { source: 'resumo' })}
            style={styles.premiumCard}
          >
            <Ionicons name="sparkles" size={20} color={colors.premium} />
            <View style={styles.rewardBody}>
              <Text style={styles.premiumTitle}>Estude sem anúncios</Text>
              <Text style={styles.rewardText}>
                Baralhos ilimitados, áudio nos dois lados e nenhum anúncio entre os treinos.
              </Text>
            </View>
            <Ionicons name="chevron-forward" size={18} color={colors.premium} />
          </Pressable>
        ) : null}

        <View style={styles.actions}>
          {remaining > 0 ? (
            <Button
              label={`Continuar — ${remaining} restantes`}
              icon="play"
              onPress={() => navigation.replace('Study', { deckId: result.deckId })}
              size="lg"
              fullWidth
            />
          ) : null}
          <Button
            label="Voltar aos baralhos"
            onPress={goHome}
            variant={remaining > 0 ? 'secondary' : 'primary'}
            size="lg"
            fullWidth
          />
        </View>
      </ScrollView>

      <InterstitialAd
        visible={adVisible}
        onClose={() => void handleAdClosed()}
        onGoPremium={() => {
          setAdVisible(false);
          setRewardPending(false);
          navigation.navigate('Paywall', { source: 'resumo' });
        }}
      />
    </Screen>
  );
}

function Row({
  label,
  value,
  total,
  color,
}: {
  label: string;
  value: number;
  total: number;
  color: string;
}) {
  return (
    <View style={styles.row}>
      <Text style={styles.rowLabel}>{label}</Text>
      <View style={styles.rowBar}>
        <ProgressBar progress={total > 0 ? value / total : 0} color={color} height={8} />
      </View>
      <Text style={[styles.rowValue, { color }]}>{value}</Text>
    </View>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricValue}>{value}</Text>
      <Text style={styles.metricLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  hero: { alignItems: 'center', gap: spacing.sm, paddingVertical: spacing.lg },
  heroIcon: {
    width: 72,
    height: 72,
    borderRadius: 36,
    backgroundColor: colors.knownSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  heroIconGoal: { backgroundColor: colors.streakSoft },
  heroTitle: { ...typography.h1, fontSize: 26, textAlign: 'center' },
  heroSubtitle: { ...typography.caption, color: colors.textMuted, textAlign: 'center' },

  streakCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.streakSoft,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  streakBody: { flex: 1, gap: 2 },
  streakTitle: { ...typography.h3 },
  streakText: { ...typography.tiny, color: colors.textMuted, lineHeight: 16 },

  goalCard: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.sm,
  },
  goalRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  goalLabel: { ...typography.h3, fontSize: 15 },
  goalValue: { ...typography.h3, fontSize: 15, color: colors.textMuted },
  goalHint: { ...typography.tiny, color: colors.textFaint },

  breakdown: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.md,
  },
  sectionTitle: { ...typography.h3, fontSize: 15 },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  rowLabel: { ...typography.caption, color: colors.textMuted, width: 104 },
  rowBar: { flex: 1 },
  rowValue: { ...typography.caption, fontWeight: '700', width: 26, textAlign: 'right' },
  summaryRow: {
    flexDirection: 'row',
    borderTopWidth: 1,
    borderTopColor: colors.border,
    paddingTop: spacing.md,
    marginTop: spacing.xs,
  },
  metric: { flex: 1, alignItems: 'center', gap: 2 },
  metricValue: { fontSize: 18, fontWeight: '700', color: colors.text },
  metricLabel: { ...typography.tiny, color: colors.textFaint },

  rewardCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.primarySoft,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  rewardBody: { flex: 1, gap: 2 },
  rewardTitle: { ...typography.h3, fontSize: 15 },
  rewardText: { ...typography.tiny, color: colors.textMuted, lineHeight: 16 },
  rewardDone: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.knownSoft,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  rewardDoneText: { ...typography.caption, color: colors.text },

  premiumCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.premiumSoft,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
  premiumTitle: { ...typography.h3, fontSize: 15, color: colors.premium },

  actions: { gap: spacing.sm },
});
