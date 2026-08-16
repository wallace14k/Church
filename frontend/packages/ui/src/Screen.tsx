import type { ReactNode } from 'react';
import { Platform, StyleSheet, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface ScreenProps {
  readonly children: ReactNode;
  readonly padded?: boolean;
  readonly style?: ViewStyle;
}

/** Largura de referência: a mesma faixa de celular para a qual o `HeroCollage`
 *  e a escala tipográfica foram calibrados. */
const LARGURA_MOVEL = 480;

/**
 * Canvas da tela.
 *
 * Branco puro, sempre. O `DESIGN.md` é explícito: a cor aparece apenas como
 * lavagem pastel em superfícies pequenas, **nunca** como fundo de seção.
 *
 * No navegador, o conteúdo fica centrado numa coluna do tamanho de um celular.
 * Sem isso, `flex: 1` estica até a largura da janela — os cartões de foto do
 * `HeroCollage`, posicionados com `left: 0`/`right: 0`, iam parar a centenas de
 * pixels um do outro, e o formulário ficava colado na borda esquerda com o
 * resto da tela em branco. O app é feito para celular; a janela larga é só o
 * jeito de olhar para ele sem um aparelho por perto.
 */
export function Screen({ children, padded = true, style }: ScreenProps) {
  const theme = useTheme();

  const conteudo = (
    <View
      style={[
        styles.screen,
        { backgroundColor: theme.colors.background },
        padded && { paddingHorizontal: theme.space[24] },
        Platform.OS === 'web' && styles.colunaMovel,
        style,
      ]}
    >
      {children}
    </View>
  );

  if (Platform.OS !== 'web') return conteudo;

  return (
    <View style={[styles.molduraWeb, { backgroundColor: theme.colors.divider }]}>{conteudo}</View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  molduraWeb: { flex: 1, alignItems: 'center' },
  colunaMovel: { width: '100%', maxWidth: LARGURA_MOVEL },
});
