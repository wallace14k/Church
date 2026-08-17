import { ActivityIndicator, Pressable, StyleSheet, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface ButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  /**
   * `outline` é a pílula secundária — **fundo transparente com fio de tinta
   * de 1px**, o tratamento que a referência dá ao "Get started" ao lado do
   * "Book a demo" em lima. A §7 é explícita ao proibir uma segunda cor
   * saturada de botão: a segunda ênfase se distingue por forma, não por cor.
   *
   * O fio é em tinta, não na cor de borda do sistema: `hairline` sobre canvas
   * mede menos de 1,3:1 e some — aceitável para dividir superfícies, não para
   * delimitar um alvo clicável.
   *
   * `ghost` é o link de texto puro, sem fundo — a ação de menor ênfase.
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
          paddingHorizontal: theme.space[24],
          borderWidth: 1,
          borderColor: theme.colors.text,
          backgroundColor: 'transparent',
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
