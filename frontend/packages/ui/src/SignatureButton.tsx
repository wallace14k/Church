import { ActivityIndicator, Pressable, StyleSheet, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface SignatureButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  readonly loading?: boolean;
  readonly disabled?: boolean;
  readonly style?: ViewStyle;
}

/**
 * Botão de ação primária.
 *
 * Pílula preenchida em índigo — a cor de marca do padrão de referência (o
 * "Send" azul-arroxeado do dashboard, o "Submit Application" do formulário).
 * É o elemento de maior peso visual da interface: **use no máximo um por
 * tela**, senão duas ações competem pela mesma atenção e nenhuma vence.
 *
 * Mantém o nome do componente (era a assinatura em latão do sistema Portrait,
 * depois o preenchimento em tinta do Steep) porque o papel é sempre o mesmo —
 * a ação primária da tela — independente de qual sistema visual está em vigor.
 * Renomear a cada troca obrigaria a tocar toda tela sem ganho real.
 */
export function SignatureButton({
  label,
  onPress,
  loading = false,
  disabled = false,
  style,
}: SignatureButtonProps) {
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
          paddingHorizontal: theme.space[20],
          backgroundColor: theme.colors.brand,
          opacity: inativo ? 0.45 : pressed ? 0.8 : 1,
        },
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={theme.colors.textOnDark} />
      ) : (
        <Text variant="bodyStrong" tone="onDark">
          {label}
        </Text>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: { alignItems: 'center', justifyContent: 'center' },
});
