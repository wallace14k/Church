import { PhotoCard } from '@congrega/ui/PhotoCard';
import { useTheme } from '@congrega/ui/theme';
import { View } from 'react-native';

/**
 * Colagem do hero da tela de entrada.
 *
 * Três polaroides inclinadas e sobrepostas, como fotos presas num mural.
 *
 * O `DESIGN.md` descreve uma dispersão maior ao redor do título, o que funciona
 * numa página de 1200px; em tela de celular a mesma dispersão empurraria o campo
 * de e-mail para baixo da dobra. Aqui a composição é comprimida num agrupamento,
 * preservando o gesto — inclinação de 3 a 6 graus, sobreposição, fio de 1px sem
 * sombra — e a função, que é dizer num olhar de que se trata o produto.
 *
 * As três cenas foram escolhidas para cobrir o alcance do ChMS sem precisar de
 * legenda: a criança no corredor (check-in infantil), o estudo em círculo
 * (células), e o aperto de mão entre gerações (a comunidade em si).
 *
 * Todas decorativas — nenhuma recebe rótulo, e o leitor de tela pula direto para
 * o título. Narrar três descrições de foto antes do campo de e-mail atrapalharia
 * quem depende dele.
 */
export function HeroCollage() {
  const theme = useTheme();

  return (
    <View style={{ height: 196, marginBottom: theme.space[24], alignItems: 'center' }}>
      <PhotoCard
        source={require('../assets/hero/r-saudacao.jpg')}
        width={112}
        height={150}
        tilt={-5.5}
        style={{ position: 'absolute', left: 0, top: 26 }}
      />

      <PhotoCard
        source={require('../assets/hero/r-circulo.jpg')}
        width={112}
        height={150}
        tilt={5}
        style={{ position: 'absolute', right: 0, top: 30 }}
      />

      {/* Central por último: fica por cima na ordem de empilhamento do RN. Sem
          isso a sobreposição fica ambígua e a composição parece desarrumada em
          vez de intencional. */}
      <PhotoCard
        source={require('../assets/hero/r-corredor.jpg')}
        width={136}
        height={182}
        tilt={-1.5}
        style={{ position: 'absolute', top: 6 }}
      />
    </View>
  );
}
