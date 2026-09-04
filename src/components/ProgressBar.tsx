import React from 'react';
import { StyleSheet, View } from 'react-native';

import { colors, radius } from '../theme';

interface Props {
  /** Entre 0 e 1. Valores fora da faixa são recortados. */
  progress: number;
  color?: string;
  height?: number;
}

export function ProgressBar({ progress, color = colors.primary, height = 6 }: Props) {
  const clamped = Math.min(1, Math.max(0, progress));
  return (
    <View style={[styles.track, { height, borderRadius: height / 2 }]}>
      <View
        style={[
          styles.fill,
          { width: `${clamped * 100}%`, backgroundColor: color, borderRadius: height / 2 },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  track: { backgroundColor: colors.surfaceAlt, overflow: 'hidden', borderRadius: radius.pill },
  fill: { height: '100%' },
});
