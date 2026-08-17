import { Pressable, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface TextLinkProps {
  readonly label: string;
  readonly onPress: () => void;
  /** Rótulo para leitor de tela quando o texto visível é curto demais ("Ver todos"). */
  readonly accessibilityLabel?: string;
  readonly style?: ViewStyle;
}

/**
 * Link de texto — a ação de menor ênfase da tela.
 *
 * **Tinta com sublinhado, não cor.** No sistema anterior estes links eram
 * índigo, e a cor sozinha carregava a informação "isto é clicável". O acento
 * atual mede 1,19:1 sobre superfície clara: repetir o padrão deixaria o link
 * invisível. A §7 já pedia outra coisa de qualquer forma — ação secundária com
 * "sublinhado ou afordância estrutural sutil".
 *
 * O ganho não é só de contraste: sublinhado funciona para quem não distingue
 * cor, e é o único afordância de link que sobrevive a um print em preto e
 * branco.
 *
 * Existe como componente, e não como estilo copiado em cada tela, porque o
 * padrão apareceu em três lugares na primeira passagem — e a quarta cópia
 * seria a que voltaria a usar cor.
 */
export function TextLink({ label, onPress, accessibilityLabel, style }: TextLinkProps) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="link"
      accessibilityLabel={accessibilityLabel ?? label}
      // O alvo de toque precisa dos 44pt mesmo quando o texto tem 18 de altura.
      hitSlop={8}
      style={({ pressed }) => [
        {
          alignSelf: 'flex-start',
          minHeight: theme.touch.minTarget,
          justifyContent: 'center',
          opacity: pressed ? 0.6 : 1,
        },
        style,
      ]}
    >
      <Text variant="captionBody" style={{ textDecorationLine: 'underline' }}>
        {label}
      </Text>
    </Pressable>
  );
}
