import { ActivityIndicator, Pressable, StyleSheet, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface ButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  /**
   * `outline` desenha borda em tinta principal; `ghost` é só texto.
   *
   * Não existe variante preenchida. Botão colorido preenchido está na lista de
   * proibições do `DESIGN.md` — a ação primária é o `RainbowButton`, e todo o
   * resto é contorno ou texto puro.
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
        {
          minHeight: theme.touch.comfortable,
          borderRadius: theme.radius.buttons,
          paddingHorizontal: theme.space[24],
          borderWidth: variant === 'outline' ? 1 : 0,
          borderColor: theme.colors.text,
          opacity: inativo ? 0.45 : pressed ? 0.7 : 1,
        },
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
