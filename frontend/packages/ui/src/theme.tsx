import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useColorScheme } from 'react-native';
import { darkColors, lightColors, motion, radius, space, touch, type, type ColorScheme } from './tokens';

export interface Theme {
  readonly colors: ColorScheme;
  readonly type: typeof type;
  readonly space: typeof space;
  readonly radius: typeof radius;
  readonly touch: typeof touch;
  readonly motion: typeof motion;
  readonly isDark: boolean;
}

function buildTheme(isDark: boolean): Theme {
  return {
    colors: isDark ? darkColors : lightColors,
    type,
    space,
    radius,
    touch,
    motion,
    isDark,
  };
}

/**
 * Tema padrão claro.
 *
 * Existe para que `useTheme` funcione fora do provider — em teste unitário e em
 * Storybook, sobretudo. A alternativa comum é lançar quando falta o provider,
 * o que transforma um esquecimento de setup em tela branca no primeiro frame.
 */
const ThemeContext = createContext<Theme>(buildTheme(false));

export interface ThemeProviderProps {
  readonly children: ReactNode;
  /** Força o esquema. Sem isso, segue a preferência do sistema. */
  readonly forceScheme?: 'light' | 'dark';
}

export function ThemeProvider({ children, forceScheme }: ThemeProviderProps) {
  const systemScheme = useColorScheme();
  const isDark = forceScheme === undefined ? systemScheme === 'dark' : forceScheme === 'dark';

  // Sem o memo, todo componente que lê o tema re-renderiza a cada render do
  // provider, porque o objeto seria novo por identidade. É o vazamento de
  // performance mais comum em Context de tema.
  const value = useMemo(() => buildTheme(isDark), [isDark]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): Theme {
  return useContext(ThemeContext);
}
