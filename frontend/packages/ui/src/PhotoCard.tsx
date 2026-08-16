import { Image, StyleSheet, View, type ImageSourcePropType, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface PhotoCardProps {
  readonly source: ImageSourcePropType;
  readonly width: number;
  readonly height: number;
  /** Inclinação em graus. O `DESIGN.md` pede de 3 a 6; valores maiores viram enfeite. */
  readonly tilt?: number;
  readonly style?: ViewStyle;
  /** Descrição para leitor de tela. Ausente = imagem decorativa, e é ignorada. */
  readonly accessibilityLabel?: string;
}

/**
 * Cartão-polaroide.
 *
 * Foto recortada fechada no sujeito, raio 24, fio de 1px e **sem sombra** — o
 * `DESIGN.md` descreve as fotos como polaroides prensadas contra o papel, não
 * flutuando sobre ele. Acrescentar sombra aqui quebra a linguagem inteira.
 *
 * Sem `accessibilityLabel` o cartão é marcado como decorativo. Isso é
 * deliberado: a colagem do hero é atmosfera, e narrar cinco descrições de foto
 * antes do campo de e-mail atrapalharia quem usa leitor de tela em vez de ajudar.
 */
export function PhotoCard({
  source,
  width,
  height,
  tilt = 0,
  style,
  accessibilityLabel,
}: PhotoCardProps) {
  const theme = useTheme();
  const decorativa = accessibilityLabel === undefined;

  return (
    <View
      style={[
        styles.card,
        {
          width,
          height,
          borderRadius: theme.radius.images,
          borderColor: theme.colors.hairline,
          backgroundColor: theme.colors.surface,
          transform: [{ rotate: `${tilt}deg` }],
        },
        style,
      ]}
      accessibilityElementsHidden={decorativa}
      importantForAccessibility={decorativa ? 'no-hide-descendants' : 'yes'}
    >
      <Image
        source={source}
        style={styles.image}
        // `cover` porque cada foto já vem recortada fechada no sujeito; `contain`
        // deixaria faixas brancas e quebraria o bloco sólido do cartão.
        resizeMode="cover"
        accessible={!decorativa}
        {...(accessibilityLabel !== undefined ? { accessibilityLabel } : {})}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderWidth: 1,
    overflow: 'hidden',
  },
  image: {
    width: '100%',
    height: '100%',
  },
});
