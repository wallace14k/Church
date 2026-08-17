import type { ReactNode } from 'react';
import { Pressable, type ViewStyle } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface ChipProps {
  readonly label: string;
  /** Detalhe secundário no mesmo rótulo — "Dízimos · Entrada". */
  readonly suffix?: string;
  readonly selected: boolean;
  /** Escolha existente mas indisponível agora — a família à qual o membro já pertence. */
  readonly disabled?: boolean;
  readonly onPress: () => void;
  /** Ícone à direita — o "x" de remover, quando o chip representa uma escolha feita. */
  readonly trailing?: ReactNode;
  readonly accessibilityLabel?: string;
  readonly style?: ViewStyle;
}

/**
 * Pílula de escolha — categoria de lançamento, coluna de importação, membro
 * vinculado.
 *
 * Existia copiada em três telas com a mesma implementação. Reunir aqui não é
 * só higiene: é onde mora a decisão D6, e ela é fácil de errar de novo.
 *
 * **Seleção é preenchimento, não borda colorida.** As três cópias marcavam o
 * selecionado com borda na cor de marca. Com o acento atual isso seria uma
 * borda de 1,19:1 contra o canvas — abaixo dos 3:1 que a WCAG 1.4.11 exige de
 * um componente não textual, ou seja, um estado que boa parte dos usuários
 * simplesmente não veria. Preenchido em lima com tinta por cima o estado mede
 * 15,5:1 e sobrevive até a um print em preto e branco.
 *
 * O lima cheio aqui não briga com o botão primário porque só um chip fica
 * selecionado por vez; o não selecionado é transparente com fio fino, o que o
 * mantém legível tanto sobre o canvas branco quanto sobre um cartão pergaminho.
 */
export function Chip({
  label,
  suffix,
  selected,
  disabled = false,
  onPress,
  trailing,
  accessibilityLabel,
  style,
}: ChipProps) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      // Anunciado ao leitor de tela: nem o preenchimento nem a opacidade
      // chegam até lá.
      accessibilityState={{ selected, disabled }}
      style={({ pressed }) => [
        {
          flexDirection: 'row',
          alignItems: 'center',
          alignSelf: 'flex-start',
          gap: theme.space[8],
          opacity: disabled ? 0.5 : pressed ? 0.75 : 1,
          borderRadius: theme.radius.tags,
          borderWidth: 1,
          borderColor: selected ? theme.colors.text : theme.colors.hairline,
          backgroundColor: selected ? theme.colors.surfaceAccent : 'transparent',
          paddingVertical: theme.space[8],
          paddingHorizontal: theme.space[16],
        },
        style,
      ]}
    >
      <Text variant="captionBody" tone={selected ? 'onAccent' : 'ink'}>
        {label}
        {suffix ? ` · ${suffix}` : ''}
      </Text>
      {trailing}
    </Pressable>
  );
}
