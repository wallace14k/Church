import { useEffect, useState } from 'react';

const CHAVE = 'congrega:sidebar-collapsed';

/**
 * Estado de recolhida/expandida da sidebar, lembrado entre sessões.
 *
 * Só existe no navegador — a sidebar em si só renderiza lá
 * (`(tabs)/_layout.web.tsx`), então `localStorage` é seguro sem checagem de
 * plataforma. `try/catch` cobre o caso raro de navegação privativa bloqueando
 * `localStorage`: degrada para "sempre expandida" em vez de quebrar a tela.
 */
export function useSidebarCollapsed(): readonly [boolean, () => void] {
  const [recolhida, setRecolhida] = useState(() => {
    try {
      return localStorage.getItem(CHAVE) === '1';
    } catch {
      return false;
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem(CHAVE, recolhida ? '1' : '0');
    } catch {
      // Preferência não persiste, mas a sessão atual continua funcionando.
    }
  }, [recolhida]);

  return [recolhida, () => setRecolhida((atual) => !atual)] as const;
}
