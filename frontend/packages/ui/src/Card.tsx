import type { ReactNode } from 'react';
import { Pressable, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface CardProps {
  readonly children: ReactNode;
  readonly onPress?: () => void;
  readonly tinted?: 'mint' | 'sky' | 'peach';
  readonly style?: ViewStyle;
  readonly accessibilityLabel?: string;
}

/**
 * Cartão.
 *
 * Superfície branca, raio 24, e um hairline de 1px no lugar de sombra. O
 * `DESIGN.md` prefere o fio à sombra quando a superfície precisa de separação —
 * é o que mantém a linguagem de papel sobre papel.
 */
export function Card({ children, onPress, tinted, style, accessibilityLabel }: CardProps) {
  const theme = useTheme();

  const fundo =
    tinted === 'mint' ? '#D7FFE2'
    : tinted === 'sky' ? '#E8F1FF'
    : tinted === 'peach' ? '#FFEBD6'
    : theme.colors.surface;

  const conteudo = (
    <View
      style={[
        {
          backgroundColor: fundo,
          borderRadius: theme.radius.cards,
          borderWidth: 1,
          borderColor: theme.colors.hairline,
          padding: theme.space[16],
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
