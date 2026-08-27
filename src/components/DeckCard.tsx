import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { colors, radius, spacing, typography } from '../theme';
import type { DeckStats } from '../store/queue';
import type { Deck } from '../types';

interface Props {
  deck: Deck;
  stats: DeckStats;
  onPress: () => void;
  onStudy: () => void;
}

export function DeckCard({ deck, stats, onPress, onStudy }: Props) {
  const nothingToDo = stats.readyNow === 0;

  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.wrapper, pressed && styles.pressed]}
      accessibilityRole="button"
      accessibilityLabel={`Baralho ${deck.name}, ${stats.readyNow} cards para estudar`}
    >
      <View style={[styles.stripe, { backgroundColor: deck.color }]} />

      <View style={styles.body}>
        <View style={styles.headerRow}>
          <Text style={styles.emoji}>{deck.emoji}</Text>
          <View style={styles.titleBlock}>
            <Text style={styles.name} numberOfLines={1}>
              {deck.name}
            </Text>
            <Text style={styles.meta}>
              {stats.total} {stats.total === 1 ? 'card' : 'cards'}
              {stats.suspended > 0 ? ` · ${stats.suspended} pausados` : ''}
            </Text>
          </View>
        </View>

        <View style={styles.countsRow}>
          <Count value={stats.newAvailable} label="novos" color={colors.primary} />
          <Count value={stats.learningDue} label="aprendendo" color={colors.partial} />
          <Count value={stats.reviewDue} label="revisar" color={colors.known} />
        </View>

        <Pressable
          onPress={onStudy}
          disabled={nothingToDo}
          style={({ pressed }) => [
            styles.studyButton,
            nothingToDo && styles.studyButtonIdle,
            pressed && !nothingToDo && styles.pressed,
          ]}
        >
          <Ionicons
            name={nothingToDo ? 'checkmark-circle-outline' : 'play'}
            size={16}
            color={nothingToDo ? colors.textMuted : '#FFFFFF'}
          />
          <Text style={[styles.studyLabel, nothingToDo && styles.studyLabelIdle]}>
            {nothingToDo ? 'Em dia por hoje' : `Estudar ${stats.readyNow}`}
          </Text>
        </Pressable>
      </View>
    </Pressable>
  );
}

function Count({ value, label, color }: { value: number; label: string; color: string }) {
  const off = value === 0;
  return (
    <View style={styles.count}>
      <Text style={[styles.countValue, { color: off ? colors.textFaint : color }]}>{value}</Text>
      <Text style={styles.countLabel}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    flexDirection: 'row',
    backgroundColor: colors.bgElevated,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
    overflow: 'hidden',
  },
  pressed: { opacity: 0.8 },
  stripe: { width: 4 },
  body: { flex: 1, padding: spacing.lg, gap: spacing.md },
  headerRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  emoji: { fontSize: 26 },
  titleBlock: { flex: 1, gap: 2 },
  name: { ...typography.h3, fontSize: 17 },
  meta: { ...typography.tiny, color: colors.textFaint },
  countsRow: { flexDirection: 'row', gap: spacing.xl },
  count: { alignItems: 'flex-start' },
  countValue: { fontSize: 19, fontWeight: '700' },
  countLabel: { ...typography.tiny, color: colors.textFaint },
  studyButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    paddingVertical: spacing.md,
  },
  studyButtonIdle: { backgroundColor: colors.surface },
  studyLabel: { fontSize: 15, fontWeight: '600', color: '#FFFFFF' },
  studyLabelIdle: { color: colors.textMuted },
});
