import type { ReactNode } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface SignatureButtonProps {
  readonly label: string;
  readonly onPress: () => void;
  readonly loading?: boolean;
  readonly disabled?: boolean;
  /**
   * Ícone à direita do rótulo — a seta de avançar da tela de entrada, por
   * exemplo. Opcional: a maioria dos botões do sistema não pede ícone nenhum,
   * e a pílula funciona igual sem ele. Quem chama já colore o ícone com
   * `theme.colors.textOnAccent` — o componente não impõe cor, só o layout.
   */
  readonly trailingIcon?: ReactNode;
  readonly style?: ViewStyle;
}

/**
 * Botão de ação primária.
 *
 * Pílula preenchida em **lima elétrico com texto em tinta** — o único acento
 * cromático do sistema, e a única ação que o usa cheio. É o elemento de maior
 * peso visual da interface: **use no máximo um por tela**, senão duas ações
 * competem pela mesma atenção e nenhuma vence.
 *
 * O texto é tinta, nunca branco: o lima é claro (luminância 0,83), e branco
 * sobre ele mede 1,4:1. A tinta mede 15,5:1. O `ActivityIndicator` segue a
 * mesma regra — um indicador branco sobre lima sumiria justamente enquanto o
 * usuário espera para saber se a ação foi aceita.
 *
 * Mantém o nome do componente porque o papel é sempre o mesmo — a ação
 * primária da tela — independente de qual sistema visual está em vigor.
 * Renomear a cada troca obrigaria a tocar toda tela sem ganho real.
 */
export function SignatureButton({
  label,
  onPress,
  loading = false,
  disabled = false,
  trailingIcon,
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
          paddingHorizontal: theme.space[24],
          backgroundColor: theme.colors.surfaceAccent,
          opacity: inativo ? 0.45 : pressed ? 0.8 : 1,
        },
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={theme.colors.textOnAccent} />
      ) : trailingIcon === undefined ? (
        <Text variant="bodyStrong" tone="onAccent">
          {label}
        </Text>
      ) : (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
          <Text variant="bodyStrong" tone="onAccent">
            {label}
          </Text>
          {trailingIcon}
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: { alignItems: 'center', justifyContent: 'center' },
});
