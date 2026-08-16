import { Text as RNText, type TextProps as RNTextProps } from 'react-native';
import { useTheme } from './theme';
import type { type as typeScale } from './tokens';

export type TextVariant = keyof typeof typeScale;

export interface TextProps extends RNTextProps {
  readonly variant?: TextVariant;
  readonly tone?: 'default' | 'muted' | 'onBrand' | 'danger' | 'accent';
}

/**
 * Texto do sistema.
 *
 * Existe para que nenhuma tela precise saber o nome de uma fonte ou um valor de
 * cor. Componente que escreve `fontSize: 17` à mão é como uma escala tipográfica
 * morre — um commit por vez.
 */
export function Text({ variant = 'body', tone = 'default', style, ...rest }: TextProps) {
  const theme = useTheme();

  const color =
    tone === 'muted' ? theme.colors.textMuted
    : tone === 'onBrand' ? theme.colors.textOnBrand
    : tone === 'danger' ? theme.colors.danger
    : tone === 'accent' ? theme.colors.accent
    : theme.colors.text;

  return <RNText {...rest} style={[theme.type[variant], { color }, style]} />;
}
