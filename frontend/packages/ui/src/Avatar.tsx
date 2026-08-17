import { View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface AvatarProps {
  readonly name: string;
  readonly size?: number;
}

/**
 * Lavagens do avatar — a única exceção à disciplina de uma voz cromática só.
 *
 * Com centenas de membros numa lista, diferenciar pessoas de relance vale mais
 * do que a regra de cor única; é o mesmo motivo pelo qual produtos como Linear
 * e Notion abrem exceção de cor exatamente aqui.
 *
 * Os tons pastel frios do sistema anterior (azul, lilás) foram trocados por
 * uma família quente na órbita do pergaminho e do lima: sob um canvas quente,
 * um avatar azul-claro é a única coisa fria da tela e chama mais atenção que o
 * botão primário. Todos são claros o bastante para a tinta passar de 12:1 em
 * cima, e nenhum aparece em qualquer outro componente.
 */
const WASHES = ['#DFF0B8', '#E4E0CF', '#D9E6DF', '#EFDFD0'] as const;

/**
 * Iniciais sobre lavagem pastel, no lugar de foto.
 *
 * A escolha da lavagem é determinística pelo nome — o mesmo membro sempre cai
 * na mesma cor entre a lista e a ficha, o que ajuda a reconhecer o cartão de
 * relance numa lista longa.
 */
export function Avatar({ name, size = 44 }: AvatarProps) {
  const theme = useTheme();
  const iniciais = pegarIniciais(name);
  const fundo = WASHES[hashSimples(name) % WASHES.length];

  return (
    <View
      style={{
        width: size,
        height: size,
        borderRadius: size / 2,
        backgroundColor: fundo,
        alignItems: 'center',
        justifyContent: 'center',
      }}
    >
      <Text
        variant="bodyStrong"
        style={{ color: theme.colors.text, fontSize: size * 0.36, letterSpacing: 0 }}
      >
        {iniciais}
      </Text>
    </View>
  );
}

function pegarIniciais(nomeCompleto: string): string {
  const partes = nomeCompleto.trim().split(/\s+/u).filter(Boolean);
  if (partes.length === 0) return '?';
  if (partes.length === 1) return partes[0]!.slice(0, 2).toUpperCase();
  return `${partes[0]![0]}${partes[partes.length - 1]![0]}`.toUpperCase();
}

function hashSimples(texto: string): number {
  let hash = 0;
  for (let i = 0; i < texto.length; i++) {
    hash = (hash * 31 + texto.charCodeAt(i)) | 0;
  }
  return Math.abs(hash);
}
