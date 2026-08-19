import { palette } from '@congrega/ui/tokens';
import { StyleSheet, View } from 'react-native';
import Svg, { Circle, Path } from 'react-native-svg';

/**
 * Pano de fundo decorativo das telas de entrada (antes do login).
 *
 * Blobs e o traço fino em curva — a mesma assinatura do site de referência do
 * sistema Perk, adaptada à paleta do produto: dois círculos preenchidos
 * sangrando pelos cantos opostos da tela, um arco fino sobre o círculo
 * superior, uma curva solta cruzando o inferior.
 *
 * **Deriva tudo de `palette.brandGreen` por opacidade**, em vez de
 * hardcodar um segundo tom de verde para o fundo. A §2 do design system
 * reserva o acento como o único cromático do sistema; um "verde de fundo" à
 * parte romperia essa disciplina logo na primeira tela que o usuário vê.
 * Ver D8 em `docs/07-design-system.md`.
 *
 * **As opacidades caíram junto com a troca do lima, e não por gosto.** O lima
 * a 35% sobre branco resultava em `#E8FFCF` — uma insinuação de cor. O verde
 * é um tom escuro: as mesmas 35% dariam `#B6D1BE`, com peso suficiente para
 * disputar atenção com o cartão de login que fica por cima. 18% no
 * preenchimento e 35% no traço reproduzem a claridade que o lima tinha. O que
 * se preserva aqui é a leitura da tela, não o número.
 *
 * Puramente decorativo: `pointerEvents="none"` para nunca capturar toque, e
 * fica atrás de tudo por ser o primeiro elemento da árvore, sob o cartão
 * branco que carrega o formulário de verdade.
 *
 * Os círculos e a curva são desenho vetorial próprio — não uma reprodução
 * pixel a pixel da referência, que não está disponível como arquivo. A forma
 * exata importa menos do que a leitura: dois blobs contidos, um traço solto,
 * tudo na mesma cor de acento.
 */
export function AuthBackdrop() {
  return (
    <View style={StyleSheet.absoluteFill} pointerEvents="none">
      <Svg
        style={StyleSheet.absoluteFill}
        viewBox="0 0 1200 800"
        preserveAspectRatio="xMidYMid slice"
      >
        {/* Canto superior direito: blob preenchido + arco fino por cima. */}
        <Circle cx={1180} cy={260} r={190} fill={palette.brandGreen} fillOpacity={0.18} />
        <Circle
          cx={1090}
          cy={95}
          r={150}
          fill="none"
          stroke={palette.brandGreen}
          strokeOpacity={0.35}
          strokeWidth={2}
        />

        {/* Canto inferior esquerdo: blob preenchido + curva solta cruzando por cima. */}
        <Circle cx={30} cy={770} r={230} fill={palette.brandGreen} fillOpacity={0.18} />
        <Path
          d="M -60 640 C 120 560, 260 760, 430 660 S 680 560, 780 660"
          fill="none"
          stroke={palette.brandGreen}
          strokeOpacity={0.35}
          strokeWidth={2}
        />
      </Svg>
    </View>
  );
}
