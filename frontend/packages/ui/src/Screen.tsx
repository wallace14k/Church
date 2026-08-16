import type { ReactNode } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface ScreenProps {
  readonly children: ReactNode;
  readonly padded?: boolean;
  readonly style?: ViewStyle;
}

/**
 * Container de tela.
 *
 * Aplica o fundo do tema. Sem ele, telas herdam o fundo transparente do RN e o
 * tema escuro mostra faixas brancas nas bordas durante a transição de navegação —
 * defeito que só aparece no aparelho, nunca no snapshot.
 */
export function Screen({ children, padded = true, style }: ScreenProps) {
  const theme = useTheme();

  return (
    <View
      style={[
        styles.screen,
        { backgroundColor: theme.colors.background },
        padded && { paddingHorizontal: theme.space.xl, paddingVertical: theme.space.lg },
        style,
      ]}
    >
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
  },
});
