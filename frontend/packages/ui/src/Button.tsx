import { ActivityIndicator, Pressable, StyleSheet, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface ButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  /**
   * `outline` é a pílula secundária — preenchida em cinza-névoa, sem borda.
   * O padrão de referência não usa botão com contorno: a segunda ênfase de
   * uma linha de ações ("Cancel", "Retake", "Back", "Skip") é sempre uma
   * pílula cinza clara ao lado da pílula índigo. `ghost` é o link de texto
   * puro, sem fundo — a ação de menor ênfase da tela.
   */
  readonly variant?: 'outline' | 'ghost';
  readonly loading?: boolean;
  readonly disabled?: boolean;
  readonly style?: ViewStyle;
}

export function Button({
  label,
  onPress,
  variant = 'outline',
  loading = false,
  disabled = false,
  style,
}: ButtonProps) {
  const theme = useTheme();
  const inativo = disabled || loading;

  return (
    <Pressable
      onPress={onPress}
      disabled={inativo}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: inativo, busy: loading }}
      style={({ pressed }) => [
        styles.button,
        variant === 'outline' && {
          minHeight: theme.touch.comfortable,
          borderRadius: theme.radius.buttons,
          paddingHorizontal: theme.space[20],
          backgroundColor: theme.colors.surfaceNeutral,
        },
        variant === 'ghost' && {
          minHeight: theme.touch.minTarget,
          paddingVertical: theme.space[4],
        },
        { opacity: inativo ? 0.45 : pressed ? 0.7 : 1 },
        style,
      ]}
    >
      {loading
        ? <ActivityIndicator color={theme.colors.text} />
        : <Text variant="bodyStrong">{label}</Text>}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: { alignItems: 'center', justifyContent: 'center' },
});
