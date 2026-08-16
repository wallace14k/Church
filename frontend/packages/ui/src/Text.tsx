import { Text as RNText, type TextProps as RNTextProps } from 'react-native';
import { useTheme } from './theme';
import type { type as escala } from './tokens';

export type TextVariant = keyof typeof escala;

export interface TextProps extends RNTextProps {
  readonly variant?: TextVariant;
  readonly tone?: 'ink' | 'body' | 'muted' | 'onDark';
}

/**
 * Texto do sistema.
 *
 * O tom padrão é `ink` — a tinta azul-marinho única que carrega todo o texto da
 * interface. `body` existe só para copy sobre lavagem pastel, onde a tinta
 * principal fica fria demais.
 */
export function Text({ variant = 'body', tone = 'ink', style, ...rest }: TextProps) {
  const theme = useTheme();

  const color =
    tone === 'muted' ? theme.colors.textMuted
    : tone === 'body' ? theme.colors.textBody
    : tone === 'onDark' ? theme.colors.textOnDark
    : theme.colors.text;

  return <RNText {...rest} style={[theme.type[variant], { color }, style]} />;
}
