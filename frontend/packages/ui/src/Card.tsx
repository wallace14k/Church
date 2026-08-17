import type { ReactNode } from 'react';
import { Pressable, View, type StyleProp, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface CardProps {
  readonly children: ReactNode;
  /**
   * `default` é o cartão do sistema: **pergaminho sobre canvas branco**, raio
   * de 28px, fio de 1px, sem sombra. `inner` é a superfície branca que vive
   * *dentro* de um cartão — o chip de valor, a linha de lista — com o raio
   * menor de 18px.
   *
   * A dupla de tons não é decorativa: a §6 proíbe sombra, então o que separa
   * cartão de página é a diferença de tom. Pintar os dois de branco deixaria
   * um fio de 1px como única separação.
   */
  readonly variant?: 'default' | 'inner';
  /**
   * Preenchimento em lima. Reservado ao bloco que precisa ser **a** coisa da
   * tela; raro de propósito, porque um segundo lima na mesma tela anula o
   * primeiro. Texto dentro dele vai em `tone="onAccent"`.
   */
  readonly tone?: 'accent';
  readonly style?: StyleProp<ViewStyle>;
  readonly accessibilityLabel?: string;
  readonly onPress?: () => void;
}

/** Cartão — pergaminho com fio fino, a superfície padrão do sistema. */
export function Card({
  children,
  onPress,
  variant = 'default',
  tone,
  style,
  accessibilityLabel,
}: CardProps) {
  const theme = useTheme();

  const fundo =
    tone === 'accent' ? theme.colors.surfaceAccent
    : variant === 'inner' ? theme.colors.surfaceInner
    : theme.colors.surface;

  const conteudo = (
    <View
      style={[
        {
          backgroundColor: fundo,
          borderRadius: variant === 'inner' ? theme.radius.smallCards : theme.radius.cards,
          // O fio existe só onde há pouca diferença de tom. Sobre lima ele
          // sujaria o bloco, e sobre a superfície interna branca dentro de um
          // cartão pergaminho o contraste tonal já resolve.
          borderWidth: variant === 'default' && tone === undefined ? 1 : 0,
          borderColor: theme.colors.hairline,
          // 24px é o mínimo que o raio de 28px comporta sem a curva comer o
          // conteúdo. Painel e cartão de métrica sobem para `panelPadding` por
          // `style`; ver D3 no documento de design.
          padding: theme.layout.cardPadding,
          gap: theme.space[4],
        },
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
