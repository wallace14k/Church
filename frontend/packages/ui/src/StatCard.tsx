import type { ReactNode } from 'react';
import { View, type ViewStyle } from 'react-native';
import { Card } from './Card';
import { Text } from './Text';
import { useTheme } from './theme';

export interface StatCardProps {
  readonly value: string;
  readonly label: string;
  readonly icon?: ReactNode;

  /**
   * Faixa de apoio em vez de métrica principal.
   *
   * O padding de painel (32px) e o valor em `headingSm` existem para o número
   * que **é** o assunto da tela — o painel de início. Numa faixa de resumo
   * acima de uma lista, esse peso compete com o conteúdo que a tela existe para
   * mostrar, e a §8 do design system pede cartão de métrica contido.
   */
  readonly compact?: boolean;

  readonly style?: ViewStyle;
}

/**
 * Cartão de métrica da §8: **valor grande, rótulo curto, espaçamento interno
 * generoso** — e nada mais. O documento é explícito ao pedir para não encher
 * cada cartão de ícone e decoração, então o `icon` continua opcional e a
 * maioria das chamadas não o passa.
 *
 * É aqui que o `panelPadding` de 32px aparece, em vez do padding de linha de
 * lista do `Card`: um número que importa precisa de respiro para carregar o
 * peso que a hierarquia lhe dá.
 */
export function StatCard({ value, label, icon, compact = false, style }: StatCardProps) {
  const theme = useTheme();

  return (
    <Card
      style={[
        {
          flex: 1,
          gap: compact ? 2 : theme.space[4],
          padding: compact ? theme.layout.cardPadding : theme.layout.panelPadding,
        },
        style,
      ]}
    >
      {icon !== undefined && <View style={{ marginBottom: theme.space[8] }}>{icon}</View>}
      <Text variant={compact ? 'subheading' : 'headingSm'}>{value}</Text>
      <Text variant="captionBody" tone="muted" numberOfLines={1}>
        {label}
      </Text>
    </Card>
  );
}
