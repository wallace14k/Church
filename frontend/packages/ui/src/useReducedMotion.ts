import { useEffect, useState } from 'react';
import { AccessibilityInfo } from 'react-native';

/**
 * O usuário pediu menos movimento?
 *
 * Cobre as duas plataformas com uma API só: no iOS/Android lê a preferência de
 * "reduzir movimento" do sistema; no web o `react-native-web` mapeia a mesma
 * chamada para `prefers-reduced-motion`.
 *
 * **Não é preferência estética.** Movimento involuntário na tela dispara
 * enjoo e vertigem em quem tem desordem vestibular — para essas pessoas a
 * animação de entrada não é um detalhe simpático, é o motivo de fechar o app.
 * Por isso a resposta certa é remover o movimento, não encurtá-lo.
 *
 * Assina a mudança em vez de ler uma vez: a preferência muda no meio da sessão
 * quando o usuário a ativa no sistema, e uma tela já montada precisa acompanhar.
 */
export function useReducedMotion(): boolean {
  const [reduzir, setReduzir] = useState(false);

  useEffect(() => {
    let ativo = true;

    void AccessibilityInfo.isReduceMotionEnabled().then((valor) => {
      if (ativo) {
        setReduzir(valor);
      }
    });

    const inscricao = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduzir);

    return () => {
      ativo = false;
      inscricao.remove();
    };
  }, []);

  return reduzir;
}
