import React from 'react';
import { StyleSheet, Text, View } from 'react-native';

import { colors, radius, spacing, typography } from '../theme';

export interface Bar {
  label: string;
  value: number;
  /** Destaca a barra (por exemplo, um dia em que a meta foi batida). */
  highlight?: boolean;
}

interface Props {
  bars: Bar[];
  color?: string;
  highlightColor?: string;
  height?: number;
  /** Texto exibido quando todos os valores são zero. */
  emptyLabel?: string;
}

/** Gráfico de barras simples, usado nas estatísticas e na previsão. */
export function BarChart({
  bars,
  color = colors.primary,
  highlightColor = colors.streak,
  height = 96,
  emptyLabel,
}: Props) {
  const max = Math.max(1, ...bars.map((bar) => bar.value));
  const isEmpty = bars.every((bar) => bar.value === 0);

  if (isEmpty && emptyLabel) {
    return (
      <View style={[styles.empty, { height }]}>
        <Text style={styles.emptyText}>{emptyLabel}</Text>
      </View>
    );
  }

  return (
    <View style={styles.wrapper}>
      <View style={[styles.plot, { height }]}>
        {bars.map((bar, index) => (
          <View key={`${bar.label}-${index}`} style={styles.column}>
            {bar.value > 0 ? <Text style={styles.value}>{bar.value}</Text> : null}
            <View
              style={[
                styles.bar,
                {
                  height: Math.max(bar.value > 0 ? 4 : 2, (bar.value / max) * (height - 22)),
                  backgroundColor: bar.value === 0
                    ? colors.surfaceAlt
                    : bar.highlight
                      ? highlightColor
                      : color,
                },
              ]}
            />
          </View>
        ))}
      </View>
      <View style={styles.labels}>
        {bars.map((bar, index) => (
          <Text key={`label-${bar.label}-${index}`} style={styles.label}>
            {bar.label}
          </Text>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.sm },
  plot: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
  column: { flex: 1, alignItems: 'center', justifyContent: 'flex-end', gap: 2 },
  value: { ...typography.tiny, color: colors.textMuted },
  bar: { width: '100%', borderRadius: radius.sm },
  labels: { flexDirection: 'row', gap: spacing.sm },
  label: { ...typography.tiny, color: colors.textFaint, flex: 1, textAlign: 'center' },
  empty: { alignItems: 'center', justifyContent: 'center' },
  emptyText: { ...typography.caption, color: colors.textFaint },
});
