import { useEffect, useState } from 'react';
import { useWindowDimensions } from 'react-native';

const CHAVE = 'congrega:sidebar-collapsed';

/**
 * Abaixo disso a sidebar expandida não cabe junto com o conteúdo.
 *
 * A expandida ocupa 240px. Numa janela de 390px — o navegador de um celular —
 * sobravam 150px para a lista, e o título dos eventos simplesmente não
 * aparecia: só os horários. É o "desktop menor" que não é experiência mobile
 * nenhuma. Em 68px de trilho de ícones sobram 322px, que é uma coluna de
 * leitura de verdade.
 */
const LARGURA_MINIMA_PARA_EXPANDIR = 900;

export interface EstadoDaSidebar {
  readonly recolhida: boolean;
  /** Alternar tem efeito? Falso quando a largura força o recolhimento. */
  readonly podeAlternar: boolean;
  readonly alternar: () => void;
}

/**
 * Estado de recolhida/expandida da sidebar.
 *
 * Combina duas fontes: a **preferência** do usuário, lembrada entre sessões, e
 * a **largura da janela**, que pode tornar a preferência impossível de honrar.
 * A largura vence — e quando ela vence, `podeAlternar` fica falso para que a
 * casca esconda o botão em vez de oferecer um controle que não faz nada.
 *
 * A preferência não é sobrescrita nesse caso: quem expandiu no desktop e
 * abriu no celular volta a ver expandida ao voltar para a tela grande.
 *
 * Só existe no navegador — a sidebar em si só renderiza lá
 * (`(tabs)/_layout.web.tsx`), então `localStorage` é seguro sem checagem de
 * plataforma. `try/catch` cobre navegação privativa bloqueando `localStorage`:
 * degrada para "sempre expandida" em vez de quebrar a tela.
 */
export function useSidebarCollapsed(): EstadoDaSidebar {
  const { width } = useWindowDimensions();
  const estreita = width < LARGURA_MINIMA_PARA_EXPANDIR;

  const [preferencia, setPreferencia] = useState(() => {
    try {
      return localStorage.getItem(CHAVE) === '1';
    } catch {
      return false;
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem(CHAVE, preferencia ? '1' : '0');
    } catch {
      // Preferência não persiste, mas a sessão atual continua funcionando.
    }
  }, [preferencia]);

  return {
    recolhida: estreita || preferencia,
    podeAlternar: !estreita,
    alternar: () => setPreferencia((atual) => !atual),
  };
}
