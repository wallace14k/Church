import { View } from 'react-native';
import { Text } from './Text';
import { useTheme } from './theme';

export interface AvatarProps {
  readonly name: string;
  readonly size?: number;
}

/**
 * Tons pastel só para o avatar — a única exceção documentada à disciplina
 * quase-monocromática do `DESIGN_new.md`. O próprio guia descreve o "Avatar
 * Bubble" com fundo "tinted (light green for JB, light blue for AF)": com
 * dezenas de membros na lista, precisar diferenciar pessoas de relance importa
 * mais do que a regra de cor única — o mesmo motivo pelo qual produtos como
 * Linear e Notion abrem exceção de cor só para avatar. Nenhum destes tons
 * aparece em nenhum outro componente do sistema.
 */
const WASHES = ['#DCEFE0', '#DCE7F7', '#F1E4F5', '#FBEFD2'] as const;

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
