import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { colors, radius, spacing, typography } from '../theme';

interface Props {
  days: number;
  /** A meta de hoje já foi batida — a chama fica acesa. */
  activeToday: boolean;
  freezes?: number;
  onPress?: () => void;
}

/** Indicador de ofensiva exibido no cabeçalho da lista de baralhos. */
export function StreakBadge({ days, activeToday, freezes = 0, onPress }: Props) {
  return (
    <Pressable
      onPress={onPress}
      disabled={!onPress}
      accessibilityRole={onPress ? 'button' : undefined}
      accessibilityLabel={`Ofensiva de ${days} ${days === 1 ? 'dia' : 'dias'}`}
      style={({ pressed }) => [styles.wrapper, pressed && onPress ? styles.pressed : null]}
    >
      <Ionicons
        name={activeToday ? 'flame' : 'flame-outline'}
        size={18}
        color={activeToday ? colors.streak : colors.textFaint}
      />
      <Text style={[styles.days, !activeToday && styles.daysIdle]}>{days}</Text>
      {freezes > 0 ? (
        <View style={styles.freezes}>
          <Ionicons name="snow" size={12} color={colors.primary} />
          <Text style={styles.freezeCount}>{freezes}</Text>
        </View>
      ) : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    backgroundColor: colors.streakSoft,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  pressed: { opacity: 0.7 },
  days: { ...typography.h3, color: colors.streak },
  daysIdle: { color: colors.textMuted },
  freezes: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 2,
    marginLeft: spacing.xs,
    paddingLeft: spacing.sm,
    borderLeftWidth: 1,
    borderLeftColor: colors.border,
  },
  freezeCount: { ...typography.tiny, color: colors.primary },
});
