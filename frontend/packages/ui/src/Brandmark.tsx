import { StyleSheet, View } from 'react-native';
import { useTheme } from './theme';

export interface BrandmarkProps {
  /** Altura total da marca em pontos. */
  readonly size?: number;
  /** Sobre superfície escura, a cruz inverte para branco. */
  readonly onDark?: boolean;
}

/**
 * Marca do Congrega, desenhada em vez de importada como imagem.
 *
 * A cruz é geometria simples — dois retângulos — e a barra abaixo dela é o
 * terceiro traço. Desenhar dispensa carregar um PNG para cada densidade de
 * tela, mantém a marca nítida em qualquer tamanho, e faz a cor vir do tema: se
 * a tinta mudar, a marca acompanha sem ninguém precisar reexportar arquivo.
 *
 * O PNG continua existindo para ícone de app e favicon, onde o sistema
 * operacional exige bitmap — esse ícone não muda com a troca de sistema de
 * design da tela, só a versão desenhada aqui dentro do app.
 *
 * **A barra perdeu o gradiente de latão** na troca do sistema Portrait para o
 * Steep: a disciplina do `DESIGN_new.md` é quase-monocromática, sem espaço
 * para uma segunda cor de assinatura além do pêssego, e o pêssego não faz
 * sentido como traço de marca sobre fundo escuro. A marca agora é um traço só,
 * na mesma tinta do resto do sistema.
 *
 * <b>Proporções</b> derivadas do ícone: a cruz ocupa 74% da altura, a barra fica
 * nos 12% inferiores, e o travessão está a 36% do topo. Fixá-las aqui é o que
 * impede a marca de deformar quando alguém muda só um valor.
 */
export function Brandmark({ size = 28, onDark = false }: BrandmarkProps) {
  const theme = useTheme();

  const cor = onDark ? theme.colors.textOnDark : theme.colors.text;

  const alturaCruz = size * 0.74;
  const espessura = Math.max(2, size * 0.13);
  const larguraTravessao = size * 0.52;
  const topoTravessao = alturaCruz * 0.36;

  return (
    <View
      style={{ width: size * 0.62, height: size }}
      accessibilityRole="image"
      accessibilityLabel="Congrega"
    >
      {/* Haste vertical */}
      <View
        style={[
          styles.absoluto,
          {
            width: espessura,
            height: alturaCruz,
            left: (size * 0.62 - espessura) / 2,
            top: 0,
            backgroundColor: cor,
          },
        ]}
      />

      {/* Travessão */}
      <View
        style={[
          styles.absoluto,
          {
            width: larguraTravessao,
            height: espessura,
            left: (size * 0.62 - larguraTravessao) / 2,
            top: topoTravessao,
            backgroundColor: cor,
          },
        ]}
      />

      {/* Barra inferior — mesmo traço da cruz, sem cor de assinatura. */}
      <View
        style={[
          styles.absoluto,
          {
            width: size * 0.52,
            height: Math.max(2, size * 0.09),
            left: (size * 0.62 - size * 0.52) / 2,
            bottom: 0,
            borderRadius: size,
            backgroundColor: cor,
          },
        ]}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  absoluto: { position: 'absolute' },
});
