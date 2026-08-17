import { View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface EyebrowPillProps {
  readonly label: string;
  /**
   * `neutral` (padrão): só texto em caixa alta, sem fundo — para contagem e
   * categoria, onde peso visual atrapalharia. `badge`: pílula com fio de 1px,
   * o tratamento da referência para etiqueta de estado — em tom neutro, não no
   * lima, porque nem todo estado é a ênfase da tela e o acento perde força a
   * cada uso.
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
        backgroundColor: 'transparent',
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        borderRadius: theme.radius.tags,
        paddingVertical: 3,
        paddingHorizontal: theme.space[12],
      }}
    >
      <Text variant="captionBody" style={{ color: theme.colors.textMuted }}>
        {label}
      </Text>
    </View>
  );
}
