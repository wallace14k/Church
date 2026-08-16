import { ActivityIndicator, Pressable, StyleSheet, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface ButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  readonly variant?: 'primary' | 'secondary' | 'danger';
  readonly loading?: boolean;
  readonly disabled?: boolean;
  readonly style?: ViewStyle;
}

/**
 * Botão.
 *
 * O rótulo é o mesmo verbo do começo ao fim do fluxo: o botão "Entrar" produz a
 * tela "Entrando"; nunca "Enviar" seguido de "Sucesso". A consistência do
 * vocabulário é o que ensina o produto a quem o usa pela primeira vez.
 */
export function Button({
  label,
  onPress,
  variant = 'primary',
  loading = false,
  disabled = false,
  style,
}: ButtonProps) {
  const theme = useTheme();
  const isDisabled = disabled || loading;

  const background =
    variant === 'primary' ? theme.colors.brand
    : variant === 'danger' ? theme.colors.danger
    : 'transparent';

  return (
    <Pressable
      onPress={onPress}
      disabled={isDisabled}
      // Estado desabilitado anunciado ao leitor de tela, não apenas pintado de
      // cinza: opacidade não é informação para quem não enxerga a tela.
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      accessibilityLabel={label}
      style={({ pressed }) => [
        styles.button,
        {
          minHeight: theme.touch.comfortable,
          borderRadius: theme.radius.md,
          paddingHorizontal: theme.space.xl,
          backgroundColor: background,
          borderWidth: variant === 'secondary' ? 1 : 0,
          borderColor: theme.colors.borderStrong,
          opacity: isDisabled ? 0.5 : pressed ? 0.85 : 1,
        },
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={variant === 'secondary' ? theme.colors.brand : theme.colors.textOnBrand} />
      ) : (
        <Text variant="bodyStrong" tone={variant === 'secondary' ? 'default' : 'onBrand'}>
          {label}
        </Text>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    alignItems: 'center',
    justifyContent: 'center',
  },
});
