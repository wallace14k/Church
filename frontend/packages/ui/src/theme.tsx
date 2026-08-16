import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { colors, elevation, layout, motion, radius, space, touch, type } from './tokens';

export interface Theme {
  readonly colors: typeof colors;
  readonly type: typeof type;
  readonly space: typeof space;
  readonly radius: typeof radius;
  readonly elevation: typeof elevation;
  readonly touch: typeof touch;
  readonly motion: typeof motion;
  readonly layout: typeof layout;
}

const theme: Theme = { colors, type, space, radius, elevation, touch, motion, layout };

/**
 * Tema do Congrega.
 *
 * **Claro apenas.** O `DESIGN_new.md` define tema claro e proíbe introduzir
 * matiz nova além do par pêssego/sienna — um modo escuro exigiria inventar uma
 * paleta inteira fora da direção. Melhor não ter modo escuro do que ter um que
 * contradiz a marca.
 *
 * O provider existe mesmo com um tema único: mantém o ponto de extensão e evita
 * que cada componente importe os tokens direto, o que tornaria um futuro segundo
 * tema uma refatoração em vez de uma configuração.
 */
const ThemeContext = createContext<Theme>(theme);

export function ThemeProvider({ children }: { readonly children: ReactNode }) {
  const value = useMemo(() => theme, []);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): Theme {
  return useContext(ThemeContext);
}
