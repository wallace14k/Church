import type { ReactNode } from 'react';
import { StyleSheet, View, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface ScreenProps {
  readonly children: ReactNode;
  readonly padded?: boolean;
  readonly style?: ViewStyle;
}

/**
 * Canvas da tela.
 *
 * Branco puro, sempre. O `DESIGN.md` é explícito: a cor aparece apenas como
 * lavagem pastel em superfícies pequenas, **nunca** como fundo de seção.
 */
export function Screen({ children, padded = true, style }: ScreenProps) {
  const theme = useTheme();

  return (
    <View
      style={[
        styles.screen,
        { backgroundColor: theme.colors.background },
        padded && { paddingHorizontal: theme.space[24] },
        style,
      ]}
    >
      {children}
    </View>
  );
}

const styles = StyleSheet.create({ screen: { flex: 1 } });
