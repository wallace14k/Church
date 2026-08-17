import { Text as RNText, type TextProps as RNTextProps } from 'react-native';
import { useTheme } from './theme';
import type { type as escala } from './tokens';

export type TextVariant = keyof typeof escala;

export interface TextProps extends RNTextProps {
  readonly variant?: TextVariant;
  /**
   * `onAccent` é o texto sobre o lima — **tinta, não branco**. Existe como tom
   * nomeado justamente para que ninguém precise lembrar disso: branco sobre
   * lima mede 1,4:1, e é o erro natural de quem vem do sistema anterior, onde
   * todo botão primário tinha texto branco.
   */
  readonly tone?: 'ink' | 'body' | 'muted' | 'onAccent' | 'onDark';
}

/**
 * Texto do sistema.
 *
 * O tom padrão é `ink` — a tinta única que carrega todo o texto da interface.
 * `muted` é o grafite do texto secundário; `body` existe para copy que precisa
 * do mesmo peso da tinta principal sobre superfície tonal.
 */
export function Text({ variant = 'body', tone = 'ink', style, ...rest }: TextProps) {
  const theme = useTheme();

  const color =
    tone === 'muted' ? theme.colors.textMuted
    : tone === 'body' ? theme.colors.textBody
    : tone === 'onAccent' ? theme.colors.textOnAccent
    : tone === 'onDark' ? theme.colors.textOnDark
    : theme.colors.text;

  return <RNText {...rest} style={[theme.type[variant], { color }, style]} />;
}
