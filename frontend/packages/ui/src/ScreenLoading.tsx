import { useEffect, useState } from 'react';
import { ActivityIndicator, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useReducedMotion } from './useReducedMotion';
import { useTheme } from './theme';

/**
 * Quanto tempo o indicador espera antes de aparecer.
 *
 * <b>250 ms, e a espera é o ponto.</b> Uma resposta que chega em 90 ms é
 * percebida como instantânea; um indicador que aparece e some dentro dessa
 * janela não informa nada — produz um piscar que rouba a atenção e faz o layout
 * saltar duas vezes em vez de uma. Acima disso a pessoa precisa saber que algo
 * está acontecendo, e aí ele entra.
 *
 * O espaço já é ocupado desde o primeiro instante, então a entrada do indicador
 * não desloca nada: o que muda é ele ficar visível, não o layout.
 */
const ATRASO_MS = 250;

export interface ScreenLoadingProps {
  /**
   * O que está sendo carregado — "o lançamento", "a ficha do membro".
   *
   * Entra na frase que o leitor de tela anuncia e no texto sob o indicador.
   * Ausente vira apenas "Carregando…", que é honesto quando a tela inteira está
   * vindo e não há uma parte a nomear.
   */
  readonly what?: string;

  /**
   * Ocupa a altura disponível e centraliza.
   *
   * Ligado é o caso da tela inteira. Desligado serve a um bloco dentro de um
   * cartão, onde centralizar verticalmente numa altura indefinida colapsaria a
   * área para zero.
   */
  readonly fill?: boolean;

  readonly style?: ViewStyle;
}

/**
 * Carregamento de tela inteira.
 *
 * Existia copiado em seis telas — <c>membros/[id]</c>, <c>agenda/[id]</c>, as
 * duas de edição, a família e o lançamento — sempre como
 * <c>&lt;Screen&gt;&lt;ActivityIndicator /&gt;&lt;/Screen&gt;</c>, e sempre com
 * os mesmos dois problemas.
 *
 * <b>O primeiro é de acessibilidade, e é o grave:</b> um <c>ActivityIndicator</c>
 * solto não tem nome acessível. Quem usa leitor de tela chega numa tela que não
 * anuncia nada, ouve silêncio e conclui que o aplicativo travou — a única
 * informação disponível é visual, e é a única que essa pessoa não recebe.
 *
 * <b>O segundo é o piscar:</b> sem atraso, uma resposta rápida faz o indicador
 * surgir e sumir em menos de um décimo de segundo, o que é ruído puro.
 *
 * Com <c>prefers-reduced-motion</c> ligado o giro some e fica só o texto. O
 * <c>ActivityIndicator</c> do React Native não tem como parar de girar — a saída
 * é não desenhá-lo, e o texto sozinho continua dizendo tudo o que ele dizia.
 */
export function ScreenLoading({ what, fill = true, style }: ScreenLoadingProps) {
  const theme = useTheme();
  const reduzirMovimento = useReducedMotion();
  const [visivel, setVisivel] = useState(false);

  useEffect(() => {
    const relogio = setTimeout(() => setVisivel(true), ATRASO_MS);

    // Sem a limpeza, uma tela que monta e desmonta rápido — navegação em
    // sequência — deixaria o temporizador vivo e chamaria `setState` sobre um
    // componente que já saiu.
    return () => clearTimeout(relogio);
  }, []);

  const frase = what === undefined ? 'Carregando…' : `Carregando ${what}…`;

  return (
    <View
      // O papel e o rótulo ficam SEMPRE, mesmo antes de o indicador aparecer:
      // o atraso existe para o olho, e quem depende do leitor de tela precisa
      // saber desde o primeiro instante que a espera começou.
      accessibilityRole="progressbar"
      accessibilityLabel={frase}
      accessibilityLiveRegion="polite"
      style={[
        {
          alignItems: 'center',
          justifyContent: 'center',
          gap: theme.space[12],
          paddingVertical: theme.space[32],
        },
        fill ? { flex: 1 } : null,
        style,
      ]}
    >
      {/* `opacity` e não montagem condicional: o bloco já ocupa o espaço desde
          o começo, então a chegada do indicador não empurra nada. */}
      <View style={{ opacity: visivel ? 1 : 0, alignItems: 'center', gap: theme.space[12] }}>
        {!reduzirMovimento && <ActivityIndicator color={theme.colors.text} />}

        <Text variant="captionBody" tone="muted">
          {frase}
        </Text>
      </View>
    </View>
  );
}
