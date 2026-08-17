import type { ReactNode } from 'react';
import { Platform, StyleSheet, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface ScreenProps {
  readonly children: ReactNode;
  readonly padded?: boolean;
  /**
   * `true` para telas que já vivem dentro de um contentor mais largo no web —
   * a área de conteúdo da sidebar, por exemplo. Sem isso, `Screen` sempre
   * comprime para a largura de celular no navegador, o que faria uma tela de
   * dashboard com grade de cartões lado a lado ficar espremida numa coluna
   * de 480px dentro de uma área que já tem 900px disponíveis.
   */
  readonly wide?: boolean;
  readonly style?: ViewStyle;
}

/** Largura de referência para telas de fluxo (login, código) fora da sidebar. */
const LARGURA_MOVEL = 480;

/**
 * Canvas da tela.
 *
 * Branco puro, sempre — a §2 coloca `#ffffff` como canvas primário, e é o tom
 * mais claro do sistema que faz o cartão pergaminho aparecer sem sombra.
 *
 * No navegador, telas de fluxo (login, código — sem sidebar ainda, o usuário
 * não está autenticado) ficam centradas numa coluna do tamanho de celular:
 * sem isso, `flex: 1` estica até a largura da janela e o formulário fica
 * colado na borda esquerda com o resto da tela em branco. Telas dentro da
 * sidebar (`wide`) já recebem uma área de conteúdo dimensionada pelo shell —
 * comprimi-las de novo aqui desperdiçaria o espaço que o layout de dashboard
 * existe para usar.
 */
export function Screen({ children, padded = true, wide = false, style }: ScreenProps) {
  const theme = useTheme();

  const conteudo = (
    <View
      style={[
        styles.screen,
        { backgroundColor: theme.colors.background },
        padded && { paddingHorizontal: theme.space[24] },
        Platform.OS === 'web' && !wide && styles.colunaMovel,
        style,
      ]}
    >
      {children}
    </View>
  );

  if (Platform.OS !== 'web' || wide) return conteudo;

  // Moldura em pergaminho, não no cinza da borda: é a mesma inversão de tom do
  // resto do sistema — a coluna de conteúdo é a superfície clara e o que a
  // cerca é o tom quente, sem precisar de sombra para separar as duas.
  return (
    <View style={[styles.molduraWeb, { backgroundColor: theme.colors.surface }]}>{conteudo}</View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1 },
  molduraWeb: { flex: 1, alignItems: 'center' },
  colunaMovel: { width: '100%', maxWidth: LARGURA_MOVEL },
});
