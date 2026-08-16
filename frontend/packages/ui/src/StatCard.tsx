import type { ReactNode } from 'react';
import { View, type ViewStyle } from 'react-native';
import { Card } from './Card';
import { Text } from './Text';
import { useTheme } from './theme';

export interface StatCardProps {
  readonly value: string;
  readonly label: string;
  readonly icon?: ReactNode;
  readonly style?: ViewStyle;
}

/**
 * "Stat Card with Chart" do `DESIGN_new.md`, na versão sem gráfico: um
 * número que importa, Sohne (Inter) em vez de Signifier — a serifada é só
 * para título editorial, nunca para dado — com o rótulo que diz o que ele
 * conta logo abaixo, em cinza auxiliar.
 */
export function StatCard({ value, label, icon, style }: StatCardProps) {
  const theme = useTheme();

  return (
    <Card style={[{ flex: 1, gap: theme.space[4] }, style]}>
      {icon !== undefined && <View style={{ marginBottom: theme.space[4] }}>{icon}</View>}
      <Text variant="subheading">{value}</Text>
      <Text variant="captionBody" tone="muted">
        {label}
      </Text>
    </Card>
  );
}
