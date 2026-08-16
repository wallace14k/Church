import { View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface EyebrowPillProps {
  readonly label: string;
  /**
   * `neutral` (padrão): só texto em caixa alta, sem fundo — para contagem e
   * categoria, onde peso visual atrapalharia. `badge`: pílula com borda fina,
   * o mesmo tratamento que o padrão de referência usa para papel de usuário
   * ("Admin", "Employee") e etiqueta de estado ("Required") — cinza, não a
   * cor de acento, porque nem todo estado é uma ênfase editorial.
   */
  readonly tone?: 'neutral' | 'badge';
}

export function EyebrowPill({ label, tone = 'neutral' }: EyebrowPillProps) {
  const theme = useTheme();

  if (tone === 'neutral') {
    return (
      <Text variant="eyebrow" tone="muted">
        {label.toUpperCase()}
      </Text>
    );
  }

  return (
    <View
      style={{
        alignSelf: 'flex-start',
        backgroundColor: theme.colors.surfaceNeutral,
        borderRadius: theme.radius.tags,
        paddingVertical: 3,
        paddingHorizontal: theme.space[8],
      }}
    >
      <Text variant="captionBody" style={{ color: theme.colors.textMuted }}>
        {label}
      </Text>
    </View>
  );
}
