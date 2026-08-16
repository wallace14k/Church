import { View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface EyebrowPillProps {
  readonly label: string;
  readonly tone?: 'mint' | 'sky' | 'peach';
}

/**
 * Etiqueta pequena de status ou categoria.
 *
 * Caixa alta, 10px, tracking de 0.14em sobre lavagem pastel. É o único lugar da
 * interface onde as lavagens aparecem — o `DESIGN.md` as reserva para chips e
 * áreas destacadas pequenas, jamais como fundo de seção.
 */
export function EyebrowPill({ label, tone = 'sky' }: EyebrowPillProps) {
  const theme = useTheme();

  const fundo = tone === 'mint' ? '#D7FFE2' : tone === 'peach' ? '#FFEBD6' : '#E8F1FF';

  return (
    <View
      style={{
        alignSelf: 'flex-start',
        backgroundColor: fundo,
        borderRadius: theme.radius.tags,
        paddingVertical: 3,
        paddingHorizontal: theme.space[8],
      }}
    >
      <Text variant="eyebrow">{label.toUpperCase()}</Text>
    </View>
  );
}
