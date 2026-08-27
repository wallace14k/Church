import React, { useMemo } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { BarChart, type Bar } from '../components/BarChart';
import { Screen } from '../components/Screen';
import { useApp } from '../store/AppContext';
import { activitySeries } from '../streak';
import { colors, radius, spacing, typography } from '../theme';
import { dayKeyToDate } from '../utils/date';

const WEEKDAYS = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];

/** Painel de progresso: ofensiva, atividade recente e previsão de revisões. */
export function StatsScreen() {
  const { cards, logs, streak, getForecast } = useApp();

  const activity: Bar[] = useMemo(
    () =>
      activitySeries(streak, 7).map((entry) => ({
        label: WEEKDAYS[dayKeyToDate(entry.day).getDay()],
        value: entry.count,
        highlight: entry.goalReached,
      })),
    [streak]
  );

  const forecastBars: Bar[] = useMemo(
    () =>
      getForecast(7).map((count, index) => ({
        label: index === 0 ? 'hoje' : `+${index}d`,
        value: count,
      })),
    [getForecast]
  );

  const totals = useMemo(() => {
    const active = cards.filter((card) => !card.suspended);
    return {
      cards: cards.length,
      learned: active.filter((card) => card.srs.state === 'review').length,
      inProgress: active.filter(
        (card) => card.srs.state === 'learning' || card.srs.state === 'relearning'
      ).length,
      untouched: active.filter((card) => card.srs.state === 'new').length,
    };
  }, [cards]);

  const last30 = useMemo(() => {
    const cutoff = Date.now() - 30 * 24 * 60 * 60 * 1000;
    const recent = logs.filter((log) => log.reviewedAt >= cutoff);
    const scored = recent.reduce(
      (sum, log) => sum + (log.grade === 'known' ? 1 : log.grade === 'partial' ? 0.5 : 0),
      0
    );
    return {
      reviews: recent.length,
      accuracy: recent.length > 0 ? scored / recent.length : 0,
    };
  }, [logs]);

  const totalReviews = streak.history
    ? Object.values(streak.history).reduce((sum, value) => sum + value, 0)
    : 0;

  return (
    <Screen>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>Progresso</Text>

        <View style={styles.streakRow}>
          <HeroStat
            icon="flame"
            value={String(streak.current)}
            label={streak.current === 1 ? 'dia seguido' : 'dias seguidos'}
            color={colors.streak}
          />
          <HeroStat
            icon="trophy"
            value={String(streak.longest)}
            label="melhor marca"
            color={colors.premium}
          />
          <HeroStat
            icon="snow"
            value={String(streak.freezes)}
            label={streak.freezes === 1 ? 'protetor' : 'protetores'}
            color={colors.primary}
          />
        </View>

        <Section title="Últimos 7 dias" subtitle="Barras laranjas são os dias em que você bateu a meta">
          <BarChart bars={activity} highlightColor={colors.streak} color={colors.surfaceAlt} />
        </Section>

        <Section title="Próximas revisões" subtitle="Quantos cards vencem em cada um dos próximos dias">
          <BarChart bars={forecastBars} emptyLabel="Nada agendado ainda" />
        </Section>

        <Section title="Sua coleção">
          <View style={styles.grid}>
            <GridStat value={totals.cards} label="cards no total" />
            <GridStat value={totals.learned} label="em revisão" color={colors.known} />
            <GridStat value={totals.inProgress} label="em aprendizado" color={colors.partial} />
            <GridStat value={totals.untouched} label="ainda não vistos" color={colors.primary} />
          </View>
        </Section>

        <Section title="Desempenho">
          <View style={styles.grid}>
            <GridStat value={totalReviews} label="respostas registradas" />
            <GridStat value={last30.reviews} label="nos últimos 30 dias" />
            <GridStat
              value={`${Math.round(last30.accuracy * 100)}%`}
              label="acerto em 30 dias"
              color={colors.known}
            />
            <GridStat value={streak.dailyGoal} label="meta diária" color={colors.streak} />
          </View>
        </Section>
      </ScrollView>
    </Screen>
  );
}

function Section({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}) {
  return (
    <View style={styles.section}>
      <View style={styles.sectionHeader}>
        <Text style={styles.sectionTitle}>{title}</Text>
        {subtitle ? <Text style={styles.sectionSubtitle}>{subtitle}</Text> : null}
      </View>
      {children}
    </View>
  );
}

function HeroStat({
  icon,
  value,
  label,
  color,
}: {
  icon: keyof typeof Ionicons.glyphMap;
  value: string;
  label: string;
  color: string;
}) {
  return (
    <View style={styles.heroStat}>
      <Ionicons name={icon} size={20} color={color} />
      <Text style={styles.heroValue}>{value}</Text>
      <Text style={styles.heroLabel}>{label}</Text>
    </View>
  );
}

function GridStat({
  value,
  label,
  color = colors.text,
}: {
  value: number | string;
  label: string;
  color?: string;
}) {
  return (
    <View style={styles.gridStat}>
      <Text style={[styles.gridValue, { color }]}>{value}</Text>
      <Text style={styles.gridLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.lg, paddingBottom: spacing.xxl },
  title: { ...typography.h1 },
  streakRow: { flexDirection: 'row', gap: spacing.sm },
  heroStat: {
    flex: 1,
    alignItems: 'center',
    gap: 2,
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    paddingVertical: spacing.lg,
  },
  heroValue: { fontSize: 22, fontWeight: '700', color: colors.text },
  heroLabel: { ...typography.tiny, color: colors.textFaint, textAlign: 'center' },
  section: {
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    padding: spacing.lg,
    gap: spacing.md,
  },
  sectionHeader: { gap: 2 },
  sectionTitle: { ...typography.h3, fontSize: 16 },
  sectionSubtitle: { ...typography.tiny, color: colors.textFaint, lineHeight: 16 },
  grid: { flexDirection: 'row', flexWrap: 'wrap' },
  gridStat: { width: '50%', paddingVertical: spacing.sm, gap: 2 },
  gridValue: { fontSize: 20, fontWeight: '700' },
  gridLabel: { ...typography.tiny, color: colors.textFaint },
});
