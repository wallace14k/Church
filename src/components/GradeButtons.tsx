import React from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { colors, radius, spacing, typography } from '../theme';
import { formatInterval } from '../utils/date';
import type { Grade } from '../types';

interface Props {
  /** Tempo, em ms, até o card voltar para cada resposta. */
  intervals: Record<Grade, number>;
  onGrade: (grade: Grade) => void;
  /** Exibe o intervalo previsto abaixo de cada rótulo. */
  showIntervals: boolean;
  disabled?: boolean;
}

const OPTIONS: { grade: Grade; label: string; color: string; background: string }[] = [
  { grade: 'forgot', label: 'Não lembro', color: colors.forgot, background: colors.forgotSoft },
  { grade: 'partial', label: 'Mais ou menos', color: colors.partial, background: colors.partialSoft },
  { grade: 'known', label: 'Lembro', color: colors.known, background: colors.knownSoft },
];

/**
 * As três respostas do treino. É a única entrada do agendador: a escolha
 * define quando o card reaparece.
 */
export function GradeButtons({ intervals, onGrade, showIntervals, disabled }: Props) {
  return (
    <View style={styles.row}>
      {OPTIONS.map((option) => (
        <Pressable
          key={option.grade}
          onPress={() => onGrade(option.grade)}
          disabled={disabled}
          accessibilityRole="button"
          accessibilityLabel={`${option.label}, volta em ${formatInterval(intervals[option.grade])}`}
          style={({ pressed }) => [
            styles.button,
            { backgroundColor: option.background, borderColor: `${option.color}55` },
            pressed && styles.pressed,
            disabled && styles.disabled,
          ]}
        >
          <Text style={[styles.label, { color: option.color }]}>{option.label}</Text>
          {showIntervals ? (
            <Text style={styles.interval}>{formatInterval(intervals[option.grade])}</Text>
          ) : null}
        </Pressable>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: spacing.sm },
  button: {
    flex: 1,
    minHeight: 64,
    borderRadius: radius.lg,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 2,
    paddingHorizontal: spacing.xs,
  },
  pressed: { opacity: 0.7 },
  disabled: { opacity: 0.4 },
  label: { fontSize: 14, fontWeight: '700', textAlign: 'center' },
  interval: { ...typography.tiny, color: colors.textFaint },
});
