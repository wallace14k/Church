import { useTheme } from '@congrega/ui/theme';
import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { AuthBackdrop } from './AuthBackdrop';

export interface AuthCardProps {
  readonly children: ReactNode;
  /** Largura máxima do cartão. Coluna única por padrão. */
  readonly maxWidth?: number;
}

const LARGURA_PADRAO = 480;

/**
 * Casca compartilhada das telas anônimas — entrar e código.
 *
 * Pano de fundo decorativo (`AuthBackdrop`) com um cartão branco flutuando
 * por cima, centralizado e rolável quando o teclado sobe.
 *
 * Existe para as duas telas terem a MESMA moldura. Sem ela, o usuário sairia
 * de "entrar" com o cartão novo e cairia em "código" com o fundo branco liso
 * de antes — uma quebra visual no meio de um fluxo de 10 segundos.
 *
 * Só cuida da moldura. O que vai dentro — coluna única ou duas colunas — é
 * decisão de cada tela: `entrar.tsx` é a única com conteúdo suficiente
 * (colagem de fotos + formulário) para abrir em duas colunas no desktop.
 */
export function AuthCard({ children, maxWidth = LARGURA_PADRAO }: AuthCardProps) {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.surface }}>
      <AuthBackdrop />

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={{ flex: 1 }}
      >
        <ScrollView
          keyboardShouldPersistTaps="handled"
          contentContainerStyle={{
            flexGrow: 1,
            alignItems: 'center',
            justifyContent: 'center',
            paddingTop: insets.top + theme.space[24],
            paddingBottom: insets.bottom + theme.space[24],
            paddingHorizontal: theme.space[20],
          }}
        >
          <View
            style={{
              width: '100%',
              maxWidth,
              backgroundColor: theme.colors.surfaceInner,
              borderRadius: theme.radius.cards,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              padding: theme.layout.panelPadding,
            }}
          >
            {children}
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}
