import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { Animated, Easing, Modal, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useReducedMotion } from './useReducedMotion';
import { useTheme } from './theme';

/**
 * O que está acontecendo, enquanto acontece.
 */
export interface CarregamentoGlobal {
  /**
   * Roda a operação com o overlay em cima, e o esconde ao fim — <b>inclusive
   * quando ela falha</b>.
   *
   * <b>É esta a forma de usar, e não o par mostrar/esconder.</b> Um par manual
   * vaza no primeiro caminho de erro que alguém esquecer de cobrir, e o vazamento
   * é da pior espécie: a tela fica bloqueada para sempre, sem nada explicando, e
   * o único caminho de volta é recarregar. O <c>finally</c> mora aqui, uma vez,
   * em vez de em cada chamada.
   */
  readonly executar: <T>(
    titulo: string,
    mensagem: string,
    operacao: () => Promise<T>,
  ) => Promise<T>;

  /** Está visível? Útil para não abrir dois overlays sobre o mesmo clique. */
  readonly visivel: boolean;
}

const Contexto = createContext<CarregamentoGlobal | null>(null);

/**
 * O carregamento global da aplicação.
 *
 * <b>É para OPERAÇÃO, não para leitura.</b> A distinção decide onde ele entra:
 *
 * <ul>
 * <li><b>Salvar, apagar, enviar, testar</b> — o overlay bloqueia a tela, e o
 *     bloqueio é o ponto. Um segundo clique em "Salvar" grava o dízimo duas
 *     vezes; sair da tela no meio de um envio de 10 MB deixa a pessoa sem saber
 *     se o comprovante subiu.</li>
 * <li><b>Abrir uma lista</b> — ali o certo é o esqueleto, que preserva o layout
 *     e não rouba o controle. Cobrir a tela para dizer "estou lendo" troca uma
 *     espera informativa por uma espera cega.</li>
 * </ul>
 *
 * <b>Sem atraso antes de aparecer</b>, ao contrário de <c>ScreenLoading</c>. Lá
 * o atraso evita um piscar; aqui ele abriria uma janela em que a tela ainda
 * aceita cliques — e um overlay que começa a bloquear 250 ms depois não bloqueia.
 */
export function GlobalLoadingProvider({ children }: { readonly children: React.ReactNode }) {
  const [estado, setEstado] = useState<{ titulo: string; mensagem: string } | null>(null);

  /**
   * Quantas operações estão em voo.
   *
   * Sem a contagem, duas operações simultâneas — salvar e recarregar a lista —
   * fariam a primeira a terminar esconder o overlay enquanto a outra ainda
   * roda, e a tela voltaria a aceitar cliques no meio de uma gravação.
   */
  const emVoo = useRef(0);

  const executar = useCallback(
    async <T,>(titulo: string, mensagem: string, operacao: () => Promise<T>): Promise<T> => {
      emVoo.current += 1;
      setEstado({ titulo, mensagem });

      try {
        return await operacao();
      } finally {
        emVoo.current -= 1;

        if (emVoo.current === 0) {
          setEstado(null);
        }
      }
    },
    [],
  );

  const valor = useMemo<CarregamentoGlobal>(
    () => ({ executar, visivel: estado !== null }),
    [executar, estado],
  );

  return (
    <Contexto.Provider value={valor}>
      {children}
      {estado !== null && <Overlay titulo={estado.titulo} mensagem={estado.mensagem} />}
    </Contexto.Provider>
  );
}

/**
 * Acesso ao carregamento global.
 *
 * Lança fora do provider, e não devolve um objeto inerte: um <c>executar</c> que
 * roda a operação sem mostrar nada seria indistinguível de funcionar, e a
 * ausência do provider só apareceria como "o loading não aparece nessa tela".
 */
export function useCarregamentoGlobal(): CarregamentoGlobal {
  const contexto = useContext(Contexto);

  if (contexto === null) {
    throw new Error(
      'useCarregamentoGlobal precisa de <GlobalLoadingProvider> acima na árvore.',
    );
  }

  return contexto;
}

function Overlay({ titulo, mensagem }: { readonly titulo: string; readonly mensagem: string }) {
  const theme = useTheme();
  const reduzirMovimento = useReducedMotion();

  return (
    <Modal
      visible
      transparent
      animationType={reduzirMovimento ? 'none' : 'fade'}
      // Sem `onRequestClose` que feche: o botão voltar do Android não pode
      // cancelar uma gravação que já saiu. O overlay some quando a operação
      // termina, e não quando o usuário desiste de esperar.
      onRequestClose={() => {}}
      // `dialog` + `aria-modal` é o que o exemplo pede, e é o que faz o leitor
      // de tela parar de percorrer o conteúdo atrás — que está inacessível de
      // qualquer forma enquanto o overlay bloqueia.
      accessibilityViewIsModal
    >
      <View
        accessibilityRole="progressbar"
        accessibilityLabel={`${titulo} ${mensagem}`}
        accessibilityLiveRegion="polite"
        style={{
          flex: 1,
          alignItems: 'center',
          justifyContent: 'center',
          padding: theme.space[24],
          // A lavagem do canvas a 68%, como no exemplo. O `backdrop-filter` do
          // CSS não existe no React Native; a opacidade sozinha já separa o
          // overlay do conteúdo, e desfoque é enfeite, não informação.
          backgroundColor: 'rgba(247, 249, 245, 0.82)',
        }}
      >
        <CartaoDeCarregamento titulo={titulo} mensagem={mensagem} />
      </View>
    </Modal>
  );
}

function CartaoDeCarregamento({
  titulo,
  mensagem,
}: {
  readonly titulo: string;
  readonly mensagem: string;
}) {
  const theme = useTheme();
  const reduzirMovimento = useReducedMotion();

  return (
    <View
      style={{
        width: '100%',
        maxWidth: 360,
        alignItems: 'center',
        paddingHorizontal: theme.space[32],
        paddingTop: theme.space[32],
        paddingBottom: theme.space[32],
        borderRadius: theme.radius.cards,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: theme.colors.surface,
        ...theme.elevation.popover,
      }}
    >
      <Anel />

      <Text variant="subheading" style={{ marginTop: theme.space[24], textAlign: 'center' }}>
        {titulo}
      </Text>

      <Text
        variant="captionBody"
        tone="muted"
        style={{ marginTop: theme.space[4], textAlign: 'center', maxWidth: 270 }}
      >
        {mensagem}
      </Text>

      {/* Barra indeterminada — DECORATIVA, e escondida do leitor de tela.
          Ela não conhece o progresso real: anunciá-la como barra de progresso
          faria a tecnologia assistiva prometer uma porcentagem que não existe.
          Com movimento reduzido ela fica parada, como o exemplo pede. */}
      {!reduzirMovimento && (
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{
            width: '100%',
            height: 4,
            marginTop: theme.space[24],
            borderRadius: theme.radius.tags,
            overflow: 'hidden',
            backgroundColor: theme.colors.surfaceInner,
          }}
        >
          <Lancadeira />
        </View>
      )}
    </View>
  );
}

/**
 * O anel: trilho parado, arco girando, ponto pulsando no centro.
 *
 * Três elementos como no exemplo, e cada um some sozinho com
 * <c>prefers-reduced-motion</c> — sobra o trilho e o ponto estáticos, que ainda
 * dizem "há algo aqui" sem movimento involuntário.
 */
function Anel() {
  const theme = useTheme();
  const reduzirMovimento = useReducedMotion();

  const giro = useRef(new Animated.Value(0)).current;
  const pulso = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    if (reduzirMovimento) return undefined;

    const rodar = Animated.loop(
      Animated.timing(giro, {
        toValue: 1,
        duration: 1000,
        easing: Easing.linear,
        useNativeDriver: true,
      }),
    );

    const pulsar = Animated.loop(
      Animated.sequence([
        Animated.timing(pulso, {
          toValue: 1,
          duration: 600,
          easing: Easing.inOut(Easing.ease),
          useNativeDriver: true,
        }),
        Animated.timing(pulso, {
          toValue: 0.7,
          duration: 600,
          easing: Easing.inOut(Easing.ease),
          useNativeDriver: true,
        }),
      ]),
    );

    rodar.start();
    pulsar.start();

    // Parar no desmonte: um loop vivo depois de o overlay sair mantém o
    // aplicativo acordado desenhando algo que ninguém vê.
    return () => {
      rodar.stop();
      pulsar.stop();
    };
  }, [reduzirMovimento, giro, pulso]);

  const rotacao = giro.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] });

  return (
    <View style={{ width: 58, height: 58, alignItems: 'center', justifyContent: 'center' }}>
      {/* Trilho */}
      <View
        style={{
          ...preencher,
          borderRadius: 29,
          borderWidth: 5,
          borderColor: theme.colors.surfaceAccentSoft,
        }}
      />

      {/* Arco. Duas bordas coloridas e duas transparentes desenham o quarto e
          meio de círculo do exemplo. */}
      <Animated.View
        style={{
          ...preencher,
          borderRadius: 29,
          borderWidth: 5,
          borderColor: 'transparent',
          borderTopColor: theme.colors.surfaceAccent,
          borderRightColor: theme.colors.surfaceAccent,
          transform: [{ rotate: reduzirMovimento ? '45deg' : rotacao }],
        }}
      />

      <Animated.View
        style={{
          width: 8,
          height: 8,
          borderRadius: 4,
          backgroundColor: theme.colors.surfaceAccent,
          opacity: reduzirMovimento ? 0.7 : pulso,
          transform: [{ scale: reduzirMovimento ? 0.85 : pulso }],
        }}
      />
    </View>
  );
}

/** A barra que atravessa o trilho. */
function Lancadeira() {
  const theme = useTheme();
  const [largura, setLargura] = useState(0);
  const posicao = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    if (largura === 0) return undefined;

    const ciclo = Animated.loop(
      Animated.timing(posicao, {
        toValue: 1,
        duration: 1500,
        easing: Easing.inOut(Easing.ease),
        useNativeDriver: true,
      }),
    );

    ciclo.start();

    return () => ciclo.stop();
  }, [largura, posicao]);

  return (
    <View style={{ flex: 1 }} onLayout={(e) => setLargura(e.nativeEvent.layout.width)}>
      <Animated.View
        style={{
          width: '35%',
          height: '100%',
          borderRadius: theme.radius.tags,
          backgroundColor: theme.colors.surfaceAccent,
          transform: [
            {
              translateX: posicao.interpolate({
                inputRange: [0, 1],
                // De fora à esquerda até fora à direita, como o exemplo.
                outputRange: [-largura * 0.4, largura],
              }),
            },
          ],
        }}
      />
    </View>
  );
}

const preencher: ViewStyle = {
  position: 'absolute',
  top: 0,
  right: 0,
  bottom: 0,
  left: 0,
};
