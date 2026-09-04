import React from 'react';
import { StyleSheet, Text, TextInput, View, type TextInputProps } from 'react-native';

import { colors, radius, spacing, typography } from '../theme';

interface Props extends TextInputProps {
  label: string;
  hint?: string;
  /** Contador de caracteres, exibido quando `maxLength` está definido. */
  showCounter?: boolean;
}

export function TextField({ label, hint, showCounter, style, ...inputProps }: Props) {
  const value = typeof inputProps.value === 'string' ? inputProps.value : '';

  return (
    <View style={styles.wrapper}>
      <View style={styles.labelRow}>
        <Text style={styles.label}>{label}</Text>
        {showCounter && inputProps.maxLength ? (
          <Text style={styles.counter}>
            {value.length}/{inputProps.maxLength}
          </Text>
        ) : null}
      </View>
      <TextInput
        placeholderTextColor={colors.textFaint}
        {...inputProps}
        style={[styles.input, inputProps.multiline && styles.multiline, style]}
      />
      {hint ? <Text style={styles.hint}>{hint}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: { gap: spacing.xs },
  labelRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  label: { ...typography.caption, color: colors.textMuted },
  counter: { ...typography.tiny },
  input: {
    backgroundColor: colors.surface,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.md,
    color: colors.text,
    fontSize: 16,
  },
  multiline: { minHeight: 96, textAlignVertical: 'top' },
  hint: { ...typography.tiny, color: colors.textFaint },
});
