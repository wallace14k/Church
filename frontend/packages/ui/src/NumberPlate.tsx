import { StyleSheet, View, Text as RNText, type ViewStyle } from 'react-native';
import { useTheme } from './theme';

export interface NumberPlateProps {
  /** O numeral. String, não number: códigos com zero à esquerda precisam sobreviver. */
  readonly value: string;
  /** Rótulo acima do encaixe. Curto — "Código de retirada", "Total do mês". */
  readonly label?: string;
  /** Texto auxiliar abaixo. */
  readonly hint?: string;
  readonly tone?: 'default' | 'brand';
  readonly style?: ViewStyle;
  /** Descrição para leitor de tela. Sem ela, cai numa leitura sensata do valor. */
  readonly accessibilityLabel?: string;
}

/**
 * Placa de números — o elemento-assinatura do Congrega.
 *
 * Referência direta ao quadro de hinos de madeira que fica na frente da igreja:
 * cartões numerados encaixados em sulcos, com numeral em latão. Aqui o encaixe
 * é sugerido por uma superfície rebaixada, um fio de latão no topo e numerais
 * monoespaçados com tracking aberto.
 *
 * **Usar com parcimônia.** Só para numerais que alguém lê em voz alta ou digita:
 * código de retirada da criança, total do dízimo do mês, número do membro. Se
 * virar container genérico de número, deixa de ser assinatura e vira decoração —
 * e a força do componente é justamente ser raro.
 */
export function NumberPlate({
  value,
  label,
  hint,
  tone = 'default',
  style,
  accessibilityLabel,
}: NumberPlateProps) {
  const theme = useTheme();
  const isBrand = tone === 'brand';

  return (
    <View
      style={[
        styles.container,
        {
          backgroundColor: isBrand ? theme.colors.brand : theme.colors.surfaceSunken,
          borderRadius: theme.radius.md,
          paddingVertical: theme.space.lg,
          paddingHorizontal: theme.space.xl,
        },
        style,
      ]}
      // Agrupa em um único nó para o leitor de tela: sem isso, o rótulo, o
      // numeral e a dica são anunciados como três elementos soltos, e o usuário
      // de VoiceOver precisa remontar a frase sozinho.
      accessible
      accessibilityRole="text"
      accessibilityLabel={accessibilityLabel ?? [label, spellOut(value), hint].filter(Boolean).join('. ')}
    >
      {/* Fio de latão — a citação do quadro de hinos. Um único traço, e nada mais. */}
      <View style={[styles.brassRule, { backgroundColor: theme.colors.accent }]} />

      {label !== undefined && (
        <RNText
          style={[
            theme.type.eyebrow,
            styles.label,
            { color: isBrand ? theme.colors.textOnBrand : theme.colors.textMuted },
          ]}
        >
          {label.toUpperCase()}
        </RNText>
      )}

      <RNText
        style={[
          theme.type.numeralLarge,
          { color: isBrand ? theme.colors.textOnBrand : theme.colors.text },
        ]}
        // O numeral é o conteúdo, não deve encolher para caber: se não couber,
        // o layout está errado, e esconder isso com fonte menor só adia o problema.
        allowFontScaling
        numberOfLines={1}
      >
        {value}
      </RNText>

      {hint !== undefined && (
        <RNText style={[theme.type.caption, { color: isBrand ? theme.colors.textOnBrand : theme.colors.textMuted }]}>
          {hint}
        </RNText>
      )}
    </View>
  );
}

/**
 * Separa o valor em dígitos para o leitor de tela.
 *
 * "482913" seria lido como "quatrocentos e oitenta e dois mil...", inútil para
 * quem precisa ditar o código ao voluntário do berçário. Dígito a dígito é como
 * a pessoa vai falar.
 */
function spellOut(value: string): string {
  return /^\d+$/u.test(value) ? value.split('').join(' ') : value;
}

const styles = StyleSheet.create({
  container: {
    alignItems: 'center',
    gap: 4,
    overflow: 'hidden',
  },
  brassRule: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    height: 2,
  },
  label: {
    marginTop: 4,
  },
});
