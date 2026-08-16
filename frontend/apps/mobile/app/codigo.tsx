import { describeError } from '@congrega/api-client/errors';
import { OTP_LENGTH, isCompleteOtp, sanitizeOtpInput } from '@congrega/core/validation';
import { Button } from '@congrega/ui/Button';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useSession } from '../src/session';

/** Segundos antes de liberar o reenvio. Espelha o limite do servidor. */
const ESPERA_REENVIO = 60;

export default function Codigo() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { confirmarCodigo, pedirCodigo } = useSession();
  const { email } = useLocalSearchParams<{ email: string }>();

  const codigo = useRef('');
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [segundos, setSegundos] = useState(ESPERA_REENVIO);

  // Contagem para o reenvio. Sem ela, o usuário toca "reenviar" repetidamente,
  // estoura o limite de 5 por 15 minutos do servidor e fica travado sem
  // entender por quê.
  useEffect(() => {
    if (segundos <= 0) return;
    const id = setTimeout(() => setSegundos((s) => s - 1), 1000);
    return () => clearTimeout(id);
  }, [segundos]);

  async function confirmar(valor: string) {
    if (!isCompleteOtp(valor)) {
      setErro(`O código tem ${OTP_LENGTH} dígitos.`);
      return;
    }

    setErro(null);
    setEnviando(true);

    try {
      await confirmarCodigo(email ?? '', valor);
      router.replace('/inicio');
    } catch (causa) {
      // O servidor devolve a mesma mensagem para código errado, expirado ou
      // tentativas esgotadas — de propósito. Não tente adivinhar o motivo.
      setErro(describeError(causa));
    } finally {
      setEnviando(false);
    }
  }

  async function reenviar() {
    setSegundos(ESPERA_REENVIO);
    setErro(null);
    try {
      await pedirCodigo(email ?? '');
    } catch (causa) {
      setErro(describeError(causa));
    }
  }

  return (
    <Screen>
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={{ flex: 1, paddingTop: insets.top + theme.space.xxxl }}
      >
        <View style={{ gap: theme.space.sm, marginBottom: theme.space.xxl }}>
          <Text variant="eyebrow" tone="accent">
            VERIFICAÇÃO
          </Text>
          <Text variant="display">Digite o código</Text>
          <Text variant="body" tone="muted">
            Enviamos {OTP_LENGTH} dígitos para {email}. O código vale por 10 minutos.
          </Text>
        </View>

        <TextField
          label="Código"
          placeholder="000000"
          defaultValue=""
          // A transformação roda no nativo, sem re-render: colar "123 456" do
          // e-mail é o caminho mais comum e precisa simplesmente funcionar.
          transform={sanitizeOtpInput}
          onValueChange={(valor) => {
            codigo.current = valor;
            if (erro !== null) setErro(null);
            // Confirma sozinho ao completar. Um botão a menos para tocar num
            // fluxo em que o usuário já está com o e-mail aberto na outra mão.
            if (isCompleteOtp(valor)) void confirmar(valor);
          }}
          {...(erro !== null ? { error: erro } : {})}
          keyboardType="number-pad"
          textContentType="oneTimeCode"
          autoComplete="one-time-code"
          maxLength={OTP_LENGTH}
          autoFocus
          inputStyle={{ letterSpacing: 8 }}
        />

        <Button
          label={enviando ? 'Entrando' : 'Entrar'}
          onPress={() => void confirmar(codigo.current)}
          loading={enviando}
          style={{ marginTop: theme.space.xl }}
        />

        <Pressable
          onPress={() => void reenviar()}
          disabled={segundos > 0}
          accessibilityRole="button"
          accessibilityState={{ disabled: segundos > 0 }}
          style={{ marginTop: theme.space.lg, minHeight: theme.touch.minTarget, justifyContent: 'center' }}
        >
          <Text variant="caption" tone={segundos > 0 ? 'muted' : 'accent'}>
            {segundos > 0 ? `Reenviar código em ${segundos}s` : 'Reenviar código'}
          </Text>
        </Pressable>
      </KeyboardAvoidingView>
    </Screen>
  );
}
