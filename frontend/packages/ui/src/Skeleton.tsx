import { useEffect, useRef } from 'react';
import { Animated, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';
import { useReducedMotion } from './useReducedMotion';

export interface SkeletonProps {
  readonly width?: number | `${number}%`;
  readonly height?: number;
  readonly radius?: number;
  readonly style?: ViewStyle;
}

/**
 * Bloco de carregamento com a forma do conteúdo que vai chegar.
 *
 * Substitui o indicador central em telas cujo layout é previsível. A diferença
 * não é estética: o spinner some e o conteúdo aparece de uma vez, deslocando
 * tudo; o esqueleto ocupa desde o início o espaço que a lista vai ocupar, e a
 * página não salta quando os dados chegam.
 *
 * **Pulsa em opacidade, não em posição.** Um brilho que atravessa o bloco é
 * movimento, e movimento involuntário é justamente o que `prefers-reduced-motion`
 * pede para não existir — com a preferência ligada, o bloco fica parado.
 */
export function Skeleton({ width = '100%', height = 16, radius, style }: SkeletonProps) {
  const theme = useTheme();
  const reduzirMovimento = useReducedMotion();
  const pulso = useRef(new Animated.Value(0.5)).current;

  useEffect(() => {
    if (reduzirMovimento) {
      pulso.setValue(0.7);
      return;
    }

    const ciclo = Animated.loop(
      Animated.sequence([
        Animated.timing(pulso, { toValue: 1, duration: 700, useNativeDriver: true }),
        Animated.timing(pulso, { toValue: 0.5, duration: 700, useNativeDriver: true }),
      ]),
    );

    ciclo.start();
    return () => ciclo.stop();
  }, [pulso, reduzirMovimento]);

  return (
    <Animated.View
      // Invisível para leitor de tela: o estado de carregamento é anunciado uma
      // vez pelo contêiner, e narrar cada retângulo seria ruído sem informação.
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      style={[
        {
          width,
          height,
          borderRadius: radius ?? theme.radius.inputs,
          backgroundColor: theme.colors.surface,
          opacity: pulso,
        },
        style,
      ]}
    />
  );
}

/**
 * Esqueleto de uma linha de lista: bloco à esquerda, duas linhas de texto.
 *
 * Espelha a forma de `LinhaDeEvento` e das linhas de membro — por isso mora
 * aqui e não em cada tela: quando a linha real mudar de proporção, o esqueleto
 * precisa mudar junto, e duas cópias divergiriam na primeira alteração.
 */
export function SkeletonListRow() {
  const theme = useTheme();

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        backgroundColor: theme.colors.surface,
        borderRadius: theme.radius.cards,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        padding: theme.layout.cardPadding,
      }}
    >
      <Skeleton width={44} height={44} radius={theme.radius.smallCards} />

      <View style={{ flex: 1, gap: theme.space[8] }}>
        <Skeleton width="55%" height={16} />
        <Skeleton width="35%" height={12} />
      </View>

      <Skeleton width={56} height={14} />
    </View>
  );
}
