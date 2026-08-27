import React from 'react';
import { StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { colors, spacing, typography } from '../theme';
import { Button } from './Button';

interface Props {
  icon: keyof typeof Ionicons.glyphMap;
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ icon, title, description, actionLabel, onAction }: Props) {
  return (
    <View style={styles.wrapper}>
      <View style={styles.iconCircle}>
        <Ionicons name={icon} size={30} color={colors.textMuted} />
      </View>
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.description}>{description}</Text>
      {actionLabel && onAction ? (
        <Button label={actionLabel} onPress={onAction} icon="add" style={styles.action} />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { alignItems: 'center', paddingHorizontal: spacing.xl, paddingVertical: spacing.xxl, gap: spacing.md },
  iconCircle: {
    width: 68,
    height: 68,
    borderRadius: 34,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: { ...typography.h3, textAlign: 'center' },
  description: { ...typography.bodyMuted, textAlign: 'center', lineHeight: 21 },
  action: { marginTop: spacing.sm },
});
