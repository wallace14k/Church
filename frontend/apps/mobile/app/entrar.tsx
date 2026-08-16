import { isProbablyEmail, normalizeEmail } from '@congrega/core/validation';
import { describeError } from '@congrega/api-client/errors';
import { RainbowButton } from '@congrega/ui/RainbowButton';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useSession } from '../src/session';

/**
 * Entrada por e-mail.
 *
 * Não há tela de "criar conta": pedir o código para um e-mail desconhecido cria
 * a conta no servidor. Uma tela a menos, e nenhuma decisão a tomar antes de
 * receber o primeiro valor do produto.
 */
export default function Entrar() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { pedirCodigo } = useSession();

  // O e-mail vive em ref, não em estado: o campo é não controlado e o valor só
  // é lido no envio. Guardá-lo em estado re-renderizaria a tela a cada tecla.
  const email = useRef('');
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar() {
    const valor = normalizeEmail(email.current);

    if (!isProbablyEmail(valor)) {
      setErro('Digite um e-mail válido.');
      return;
    }

    setErro(null);
    setEnviando(true);

    try {
      await pedirCodigo(valor);
      // Avança sempre que a chamada não falhar. O servidor responde 202 mesmo
      // para e-mail inexistente — distinguir aqui entregaria ao atacante a
      // lista de quem tem conta.
      router.push({ pathname: '/codigo', params: { email: valor } });
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Screen>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={{ flex: 1, paddingTop: insets.top + theme.space[48] }}
      >
        <View style={{ gap: theme.space[8], marginBottom: theme.space[32] }}>
          <Text variant="eyebrow" tone="muted">
            CONGREGA
          </Text>
          <Text variant="headingLg">Bom te ver por aqui</Text>
          <Text variant="body" tone="muted">
            Informe seu e-mail e enviaremos um código de acesso. Não precisa de senha.
          </Text>
        </View>

        <TextField
          label="E-mail"
          placeholder="voce@exemplo.com"
          defaultValue=""
          onValueChange={(valor) => {
            email.current = valor;
            if (erro !== null) setErro(null);
          }}
          {...(erro !== null ? { error: erro } : {})}
          keyboardType="email-address"
          autoCapitalize="none"
          autoComplete="email"
          textContentType="emailAddress"
          autoCorrect={false}
          returnKeyType="go"
          onSubmitEditing={() => void enviar()}
          // Foco automático: o teclado já sobe e o usuário digita direto. Numa
          // tela de um campo só, qualquer toque a mais é atrito puro.
          autoFocus
        />

        <RainbowButton
          label={enviando ? 'Enviando' : 'Enviar código'}
          onPress={() => void enviar()}
          loading={enviando}
          style={{ marginTop: theme.space[24] }}
        />

        <Text variant="caption" tone="muted" style={{ marginTop: theme.space[16] }}>
          Ao continuar, você concorda com os termos de uso e com a política de privacidade.
        </Text>
      </KeyboardAvoidingView>
    </Screen>
  );
}
