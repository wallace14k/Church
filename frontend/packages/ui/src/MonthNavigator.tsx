import { Feather } from '@expo/vector-icons';
import { useState } from 'react';
import { Pressable, View, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface MonthNavigatorProps {
  /** Rótulo já formatado — "agosto de 2026". */
  readonly label: string;

  /** Passos a partir do mês atual: `-1` anterior, `+1` próximo. */
  readonly onChange: (steps: number) => void;

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
 * Navegação de mês — seta, período, seta.
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
  onChange,
  onToday,
  isCurrentMonth = true,
  block = false,
  style,
}: MonthNavigatorProps) {
  const theme = useTheme();

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
    </View>
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
