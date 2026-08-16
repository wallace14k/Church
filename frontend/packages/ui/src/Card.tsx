import type { ReactNode } from 'react';
import { Pressable, View, type StyleProp, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface CardProps {
  readonly children: ReactNode;
  readonly onPress?: () => void;
  /**
   * `default` é o cartão do sistema inteiro: branco, borda de 1px, sombra
   * quase imperceptível — o padrão de referência não distingue "cartão
   * estático" de "artefato flutuante", os dois são a mesma superfície.
   * `muted` é o preenchimento cinza-névoa, reservado para bloco secundário
   * dentro de um cartão (ex.: resumo de card de crédito dentro da ficha).
   */
  readonly variant?: 'default' | 'muted';
  /**
   * Realce indigo — o único acento cromático do sistema, usado em blocos que
   * precisam se destacar (texto de certificação legal, aviso). Raro de
   * propósito.
   */
  readonly tinted?: 'indigo';
  readonly style?: StyleProp<ViewStyle>;
  readonly accessibilityLabel?: string;
}

/** Cartão — branco com borda fina, a superfície padrão do sistema. */
export function Card({ children, onPress, variant = 'default', tinted, style, accessibilityLabel }: CardProps) {
  const theme = useTheme();

  const fundo = tinted === 'indigo' ? theme.colors.surfaceTinted
    : variant === 'muted' ? theme.colors.surfaceNeutral
    : theme.colors.surface;

  const conteudo = (
    <View
      style={[
        {
          backgroundColor: fundo,
          borderRadius: theme.radius.cards,
          borderWidth: variant === 'default' && tinted === undefined ? 1 : 0,
          borderColor: theme.colors.hairline,
          padding: theme.space[16],
          gap: theme.space[4],
        },
        variant === 'default' && tinted === undefined && theme.elevation.floating,
        style,
      ]}
    >
      {children}
    </View>
  );

  if (onPress === undefined) {
    return conteudo;
  }

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      style={({ pressed }) => ({ opacity: pressed ? 0.8 : 1 })}
    >
      {conteudo}
    </Pressable>
  );
}
