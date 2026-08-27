import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
  type ViewStyle,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';

import { colors, radius, spacing } from '../theme';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'premium';
type Size = 'md' | 'lg';

interface Props {
  label: string;
  onPress: () => void;
  variant?: Variant;
  size?: Size;
  icon?: keyof typeof Ionicons.glyphMap;
  disabled?: boolean;
  loading?: boolean;
  fullWidth?: boolean;
  style?: ViewStyle;
}

export function Button({
  label,
  onPress,
  variant = 'primary',
  size = 'md',
  icon,
  disabled,
  loading,
  fullWidth,
  style,
}: Props) {
  const isDisabled = disabled || loading;
  const palette = PALETTE[variant];

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: Boolean(isDisabled), busy: Boolean(loading) }}
      onPress={onPress}
      disabled={isDisabled}
      style={({ pressed }) => [
        styles.base,
        size === 'lg' && styles.large,
        { backgroundColor: palette.bg, borderColor: palette.border },
        fullWidth && styles.fullWidth,
        pressed && styles.pressed,
        isDisabled && styles.disabled,
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={palette.text} size="small" />
      ) : (
        <View style={styles.content}>
          {icon ? <Ionicons name={icon} size={size === 'lg' ? 20 : 17} color={palette.text} /> : null}
          <Text style={[styles.label, size === 'lg' && styles.labelLarge, { color: palette.text }]}>
            {label}
          </Text>
        </View>
      )}
    </Pressable>
  );
}

const PALETTE: Record<Variant, { bg: string; text: string; border: string }> = {
  primary: { bg: colors.primary, text: '#FFFFFF', border: colors.primary },
  secondary: { bg: colors.surface, text: colors.text, border: colors.border },
  ghost: { bg: 'transparent', text: colors.textMuted, border: 'transparent' },
  danger: { bg: colors.forgotSoft, text: colors.forgot, border: 'transparent' },
  premium: { bg: colors.premium, text: '#1A1500', border: colors.premium },
};

const styles = StyleSheet.create({
  base: {
    minHeight: 46,
    paddingHorizontal: spacing.lg,
    borderRadius: radius.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  large: { minHeight: 54, borderRadius: radius.lg },
  fullWidth: { alignSelf: 'stretch' },
  pressed: { opacity: 0.75 },
  disabled: { opacity: 0.45 },
  content: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  label: { fontSize: 15, fontWeight: '600' },
  labelLarge: { fontSize: 17 },
});
