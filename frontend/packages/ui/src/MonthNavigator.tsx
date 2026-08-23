import { Feather } from '@expo/vector-icons';
import { useEffect, useState } from 'react';
import { Modal, Pressable, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

/**
 * Nomes curtos, para o quadro de doze.
 *
 * Abreviados porque a grade tem três colunas e "setembro" por extenso quebraria
 * a linha ou encolheria a fonte — e um quadro de meses em que os rótulos não
 * alinham deixa de ser lido de relance, que é a única razão de ele existir.
 */
const MESES_CURTOS = [
  'jan', 'fev', 'mar', 'abr', 'mai', 'jun',
  'jul', 'ago', 'set', 'out', 'nov', 'dez',
] as const;

const MESES_LONGOS = [
  'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
  'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
] as const;

/**
 * Quantos anos para trás o seletor alcança.
 *
 * Dez: mais do que qualquer igreja terá neste sistema, e finito. Sem limite, é
 * possível chegar a 1998 e concluir que o sistema perdeu os lançamentos — um
 * mês vazio parece exatamente igual a um mês perdido.
 */
const ANOS_PARA_TRAS = 10;

/**
 * Quantos anos para a frente.
 *
 * Dois, e o primeiro não é folga: uma série periódica criada em dezembro gera
 * parcelas **previstas** até dezembro do ano seguinte
 * (<c>GivingEntry.HorizonteEmMeses = 12</c>). Se o seletor parasse no ano
 * corrente, essas parcelas existiriam no banco e seriam inalcançáveis pela tela.
 */
const ANOS_PARA_FRENTE = 2;

/**
 * Estado de hover por eventos, e não pelo `hovered` do callback de estilo.
 *
 * `hovered` existe no react-native-web e não na tipagem do React Native — usá-lo
 * compila no navegador e quebra a checagem de tipos do pacote compartilhado. Os
 * eventos existem nos dois.
 */
function useHover() {
  const [emHover, setEmHover] = useState(false);

  return {
    emHover,
    props: {
      onHoverIn: () => setEmHover(true),
      onHoverOut: () => setEmHover(false),
    },
  };
}

export interface MonthNavigatorProps {
  /** Rótulo já formatado — "agosto de 2026". */
  readonly label: string;

  /** Ano exibido. Posiciona o seletor ao abrir. */
  readonly year: number;

  /** Mês exibido, de 1 a 12. */
  readonly month: number;

  /** Passos a partir do mês atual: `-1` anterior, `+1` próximo. */
  readonly onChange: (steps: number) => void;

  /**
   * Salto direto para um mês.
   *
   * <b>Obrigatório, e não opcional.</b> O ícone de calendário sempre pareceu um
   * botão e não era nada — quem quisesse ver março do ano passado clicava nele,
   * não acontecia nada, e sobravam dezessete toques na seta. Tornando a
   * propriedade obrigatória, o ícone nunca volta a ser decorativo por descuido.
   */
  readonly onSelect: (year: number, month: number) => void;

  /**
   * Volta ao mês corrente.
   *
   * Só é oferecido quando o usuário **não** está nele — um "Hoje" que não leva
   * a lugar nenhum é ruído, e some sozinho quando cumpre o papel.
   */
  readonly onToday?: () => void;

  /** O mês exibido é o corrente? Decide se "Hoje" aparece. */
  readonly isCurrentMonth?: boolean;

  /** Ocupa a largura toda, como faixa própria acima do conteúdo. */
  readonly block?: boolean;

  readonly style?: ViewStyle;
}

/**
 * Navegação de mês — seta, período, seta; e o período abre um seletor.
 *
 * Existia copiada em três telas (agenda, caixa e fechamento), com o mesmo
 * defeito nas três: `justifyContent: 'space-between'` sobre a largura inteira
 * da página, que jogava as setas contra bordas opostas e deixava o rótulo
 * boiando num vão de mil pixels — três elementos de **um só** controle,
 * separados por uma varredura de olhos.
 *
 * `block` é a variante em faixa: as setas vão para as pontas de um cartão
 * próprio, com o período no centro. Aqui o vão é intencional e delimitado pela
 * borda do cartão, que é o que faltava antes — a faixa diz onde o controle
 * começa e termina.
 *
 * **Hover e foco visíveis**, que as três cópias não tinham: mudavam só a
 * opacidade ao pressionar, o que não existe para quem navega por teclado.
 */
export function MonthNavigator({
  label,
  year,
  month,
  onChange,
  onSelect,
  onToday,
  isCurrentMonth = true,
  block = false,
  style,
}: MonthNavigatorProps) {
  const theme = useTheme();
  const [seletorAberto, setSeletorAberto] = useState(false);
  const hoverDoPeriodo = useHover();

  const miolo = (
    <>
      <Seta direcao="chevron-left" rotulo="Mês anterior" onPress={() => onChange(-1)} />

      <View
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'center',
          gap: theme.space[8],
          ...(block ? { flex: 1 } : {}),
        }}
      >
        {/* O período INTEIRO é o gatilho — ícone e texto juntos.
            Um alvo do tamanho de um ícone de 16px é difícil de acertar no
            celular, e o rótulo ao lado já é para onde o olho vai. */}
        <Pressable
          onPress={() => setSeletorAberto(true)}
          accessibilityRole="button"
          accessibilityLabel={`${label}. Escolher outro mês`}
          {...hoverDoPeriodo.props}
          style={({ pressed }) => ({
            flexDirection: 'row',
            alignItems: 'center',
            gap: theme.space[8],
            paddingVertical: theme.space[4],
            paddingHorizontal: theme.space[8],
            borderRadius: theme.radius.tags,
            backgroundColor:
              hoverDoPeriodo.emHover || pressed ? theme.colors.surface : 'transparent',
            ...(block ? { flex: 1, justifyContent: 'center' } : {}),
          })}
        >
          <Feather name="calendar" size={16} color={theme.colors.textMuted} />

          {/* `liveRegion` anuncia a troca: quem usa leitor de tela ouve "Mês
              anterior" ao acionar o botão, mas sem isto nunca fica sabendo em que
              mês parou — o foco continua no botão e o rótulo muda em silêncio. */}
          <Text
            variant="bodyStrong"
            accessibilityLiveRegion="polite"
            // Largura mínima só na variante compacta: sem ela as setas dançam ao
            // trocar de "maio" para "setembro", e o alvo foge do cursor de quem
            // clica várias vezes seguidas.
            style={block ? undefined : { minWidth: 152, textAlign: 'center' }}
          >
            {label}
          </Text>

          <Feather name="chevron-down" size={14} color={theme.colors.textMuted} />
        </Pressable>

        {onToday !== undefined && !isCurrentMonth && (
          <Pressable
            onPress={onToday}
            accessibilityRole="button"
            accessibilityLabel="Voltar ao mês atual"
            hitSlop={8}
            style={({ pressed }) => ({
              marginLeft: theme.space[4],
              paddingVertical: theme.space[4],
              paddingHorizontal: theme.space[12],
              borderRadius: theme.radius.tags,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              opacity: pressed ? 0.7 : 1,
            })}
          >
            <Text variant="captionBody" tone="muted">
              Hoje
            </Text>
          </Pressable>
        )}
      </View>

      <Seta direcao="chevron-right" rotulo="Próximo mês" onPress={() => onChange(1)} />
    </>
  );

  return (
    <View
      style={[
        {
          flexDirection: 'row',
          alignItems: 'center',
          gap: theme.space[4],
        },
        block
          ? {
              backgroundColor: theme.colors.surfaceInner,
              borderRadius: theme.radius.cards,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              paddingHorizontal: theme.space[8],
              paddingVertical: theme.space[4],
            }
          : { alignSelf: 'flex-start' },
        style,
      ]}
    >
      {miolo}

      <SeletorDeMes
        aberto={seletorAberto}
        year={year}
        month={month}
        onFechar={() => setSeletorAberto(false)}
        onEscolher={(a, m) => {
          onSelect(a, m);
          setSeletorAberto(false);
        }}
      />
    </View>
  );
}

/**
 * O quadro de meses, com o ano no topo.
 *
 * <b>`Modal`, e não um painel absoluto.</b> Em React Native não existe
 * `z-index` confiável entre irmãos de árvores diferentes: um painel posicionado
 * ficaria atrás do conteúdo seguinte, ou seria recortado pelo `ScrollView` da
 * página. É o mesmo motivo que levou o `Dropdown` ao `Modal`.
 *
 * <b>Dois anos de distância, não doze meses.</b> Trocar o ano é um passo
 * separado de escolher o mês porque a pergunta é assim: quem procura março de
 * 2024 sabe o ano antes de saber o mês. Uma lista corrida de cento e quarenta
 * meses obrigaria a rolar por um eixo em que o alvo não tem marco visual.
 */
function SeletorDeMes({
  aberto,
  year,
  month,
  onFechar,
  onEscolher,
}: {
  readonly aberto: boolean;
  readonly year: number;
  readonly month: number;
  readonly onFechar: () => void;
  readonly onEscolher: (year: number, month: number) => void;
}) {
  const theme = useTheme();

  const hoje = new Date();
  const anoAtual = hoje.getFullYear();
  const mesAtual = hoje.getMonth() + 1;

  const anoMinimo = anoAtual - ANOS_PARA_TRAS;
  const anoMaximo = anoAtual + ANOS_PARA_FRENTE;

  const [anoVisivel, setAnoVisivel] = useState(year);

  // Reabrir precisa mostrar o ano em que o usuário ESTÁ, não o que ele estava
  // folheando quando desistiu da última vez. Sem isto, quem abre, vai até 2021,
  // fecha sem escolher e abre de novo é recebido por 2021 — e conclui que
  // navegou sem querer.
  useEffect(() => {
    if (aberto) {
      setAnoVisivel(year);
    }
  }, [aberto, year]);

  const podeVoltar = anoVisivel > anoMinimo;
  const podeAvancar = anoVisivel < anoMaximo;

  return (
    <Modal visible={aberto} transparent animationType="fade" onRequestClose={onFechar}>
      {/* Toque fora fecha. É o gesto esperado, e sem ele o Android depende só
          do botão voltar — que o `onRequestClose` cobre, mas o iOS não tem. */}
      <Pressable
        onPress={onFechar}
        accessibilityLabel="Fechar seletor de mês"
        style={{
          flex: 1,
          justifyContent: 'center',
          alignItems: 'center',
          padding: theme.space[24],
          backgroundColor: 'rgba(20, 20, 15, 0.35)',
        }}
      >
        {/* Pressable interno que não faz nada: intercepta o toque para que
            escolher um mês não feche pelo backdrop antes do `onEscolher`. */}
        <Pressable
          onPress={() => {}}
          style={{
            width: '100%',
            maxWidth: 360,
            borderRadius: theme.radius.cards,
            backgroundColor: theme.colors.surface,
            borderWidth: 1,
            borderColor: theme.colors.hairline,
            overflow: 'hidden',
            ...theme.elevation.popover,
          }}
        >
          {/* ------------------------------------------------------ o ano */}
          <View
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              justifyContent: 'space-between',
              paddingHorizontal: theme.space[12],
              paddingVertical: theme.space[12],
              borderBottomWidth: 1,
              borderBottomColor: theme.colors.hairline,
            }}
          >
            <SetaDeAno
              direcao="chevron-left"
              rotulo="Ano anterior"
              habilitada={podeVoltar}
              onPress={() => setAnoVisivel((a) => a - 1)}
            />

            <Text variant="bodyStrong" accessibilityLiveRegion="polite">
              {anoVisivel}
            </Text>

            <SetaDeAno
              direcao="chevron-right"
              rotulo="Próximo ano"
              habilitada={podeAvancar}
              onPress={() => setAnoVisivel((a) => a + 1)}
            />
          </View>

          {/* --------------------------------------------------- os meses */}
          <View
            accessibilityRole="radiogroup"
            style={{
              flexDirection: 'row',
              flexWrap: 'wrap',
              padding: theme.space[8],
            }}
          >
            {MESES_CURTOS.map((abreviado, indice) => {
              const numero = indice + 1;
              const escolhido = anoVisivel === year && numero === month;
              const ehHoje = anoVisivel === anoAtual && numero === mesAtual;

              return (
                <MesDoQuadro
                  key={abreviado}
                  abreviado={abreviado}
                  longo={MESES_LONGOS[indice] ?? abreviado}
                  ano={anoVisivel}
                  escolhido={escolhido}
                  ehHoje={ehHoje}
                  onPress={() => onEscolher(anoVisivel, numero)}
                />
              );
            })}
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

function MesDoQuadro({
  abreviado,
  longo,
  ano,
  escolhido,
  ehHoje,
  onPress,
}: {
  readonly abreviado: string;
  readonly longo: string;
  readonly ano: number;
  readonly escolhido: boolean;
  readonly ehHoje: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();
  const hover = useHover();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ checked: escolhido }}
      // Nome longo e ano juntos: "ago" lido em voz alta não é uma data, e o
      // leitor de tela não tem o cabeçalho do quadro como contexto.
      accessibilityLabel={`${longo} de ${ano}${ehHoje ? ', mês atual' : ''}`}
      {...hover.props}
      style={({ pressed }) => ({
        width: '33.33%',
        paddingVertical: theme.space[12],
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: theme.radius.smallCards,
        backgroundColor: escolhido
          ? theme.colors.surfaceAccent
          : hover.emHover || pressed
            ? theme.colors.surfaceInner
            : 'transparent',
      })}
    >
      <Text
        variant="bodyStrong"
        style={escolhido ? { color: theme.colors.textOnAccent } : undefined}
      >
        {abreviado}
      </Text>

      {/* O mês corrente ganha uma MARCA, e o escolhido ganha o preenchimento.
          São dois estados diferentes e precisam de dois sinais diferentes:
          depois de navegar para março, "onde estou" e "onde é hoje" deixam de
          ser a mesma célula. Um ponto colorido sozinho não serviria — quem não
          distingue matiz não veria nada. */}
      {ehHoje && (
        <Text
          variant="captionBody"
          style={{
            marginTop: 2,
            color: escolhido ? theme.colors.textOnAccent : theme.colors.textMuted,
          }}
        >
          hoje
        </Text>
      )}
    </Pressable>
  );
}

function SetaDeAno({
  direcao,
  rotulo,
  habilitada,
  onPress,
}: {
  readonly direcao: 'chevron-left' | 'chevron-right';
  readonly rotulo: string;
  readonly habilitada: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={habilitada ? onPress : undefined}
      disabled={!habilitada}
      accessibilityRole="button"
      accessibilityLabel={rotulo}
      accessibilityState={{ disabled: !habilitada }}
      style={({ pressed }) => ({
        width: theme.touch.minTarget,
        height: theme.touch.minTarget,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: theme.radius.buttons,
        backgroundColor: pressed && habilitada ? theme.colors.surfaceInner : 'transparent',
        // O limite fica VISÍVEL. Uma seta que parece clicável e não faz nada é
        // pior do que uma seta apagada: a primeira leva a clicar de novo.
        opacity: habilitada ? 1 : 0.3,
      })}
    >
      <Feather name={direcao} size={20} color={theme.colors.text} />
    </Pressable>
  );
}

function Seta({
  direcao,
  rotulo,
  onPress,
}: {
  readonly direcao: 'chevron-left' | 'chevron-right';
  readonly rotulo: string;
  readonly onPress: () => void;
}) {
  const theme = useTheme();
  const [emHover, setEmHover] = useState(false);
  const [comFoco, setComFoco] = useState(false);

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={rotulo}
      onHoverIn={() => setEmHover(true)}
      onHoverOut={() => setEmHover(false)}
      onFocus={() => setComFoco(true)}
      onBlur={() => setComFoco(false)}
      style={({ pressed }) => ({
        width: theme.touch.minTarget,
        height: theme.touch.minTarget,
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: theme.radius.buttons,

        // Lavagem tonal no hover, não cor nova: a §7 do design system proíbe uma
        // segunda cor saturada de ação, e o acento lima é do botão primário.
        backgroundColor: emHover || pressed ? theme.colors.surface : 'transparent',

        // O anel usa tinta, não a cor de borda do sistema: `hairline` sobre o
        // canvas mede menos de 1,3:1 e some — aceitável para dividir superfície,
        // não para marcar onde o teclado está.
        borderWidth: 1,
        borderColor: comFoco ? theme.colors.text : 'transparent',

        opacity: pressed ? 0.7 : 1,
      })}
    >
      <Feather name={direcao} size={20} color={theme.colors.text} />
    </Pressable>
  );
}
