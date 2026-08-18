import { isProbablyEmail, normalizeEmail } from '@congrega/core/validation';
import { describeError } from '@congrega/api-client/errors';
import { Brandmark } from '@congrega/ui/Brandmark';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { View, useWindowDimensions } from 'react-native';
import { AuthCard } from '../src/AuthCard';
import { HeroCollage } from '../src/HeroCollage';
import { useSession } from '../src/session';

/** Abaixo disso o cartão empilha em coluna única — a largura de um celular. */
const LARGURA_QUEBRA_DUAS_COLUNAS = 860;
const LARGURA_MAXIMA_COLUNA_UNICA = 480;
const LARGURA_MAXIMA_DUAS_COLUNAS = 1040;

/**
 * Entrada por e-mail.
 *
 * Não há tela de "criar conta": pedir o código para um e-mail desconhecido cria
 * a conta no servidor. Uma tela a menos, e nenhuma decisão a tomar antes de
 * receber o primeiro valor do produto — é por isso que a linha "primeira vez
 * por aqui" abaixo não aponta para nenhum contato: não existe fluxo separado
 * para entrar.
 *
 * **Cartão branco sobre pano de fundo pergaminho com blobs** (`AuthCard`), em
 * vez do canvas branco liso do resto do app: esta é a única dupla de telas
 * (esta e `/codigo`) onde a hierarquia de superfície da §2 se inverte de
 * propósito — aqui o pergaminho com acento é o "canvas" da marca, e o branco
 * é o cartão elevado que carrega a função. Acima de
 * `LARGURA_QUEBRA_DUAS_COLUNAS` o cartão abre em duas colunas (apresentação à
 * esquerda, formulário à direita, com um fio vertical entre as duas); abaixo
 * disso — todo celular, e a maioria das janelas de navegador — empilha em
 * coluna única.
 */
export default function Entrar() {
  const theme = useTheme();
  const { pedirCodigo } = useSession();
  const { width: larguraJanela } = useWindowDimensions();
  const duasColunas = larguraJanela >= LARGURA_QUEBRA_DUAS_COLUNAS;

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
    <AuthCard maxWidth={duasColunas ? LARGURA_MAXIMA_DUAS_COLUNAS : LARGURA_MAXIMA_COLUNA_UNICA}>
      <View
        style={{
          flexDirection: duasColunas ? 'row' : 'column',
          alignItems: duasColunas ? 'center' : 'stretch',
          gap: duasColunas ? theme.space[48] : theme.space[24],
        }}
      >
        <View style={{ flex: duasColunas ? 1 : undefined, gap: theme.space[8] }}>
          <HeroCollage />
          <Brandmark size={34} />
          <Text variant="headingLg">Bom te ver por aqui</Text>
          <Text variant="body" tone="muted">
            Informe seu e-mail e enviaremos um código de acesso. Não precisa de senha.
          </Text>
        </View>

        {duasColunas && (
          <View style={{ width: 1, alignSelf: 'stretch', backgroundColor: theme.colors.hairline }} />
        )}

        <View style={{ flex: duasColunas ? 1 : undefined, gap: theme.space[16] }}>
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
            // Foco automático: o teclado já sobe e o usuário digita direto.
            // Numa tela de um campo só, qualquer toque a mais é atrito puro.
            autoFocus
          />

          <SignatureButton
            label={enviando ? 'Enviando' : 'Enviar código'}
            onPress={() => void enviar()}
            loading={enviando}
            trailingIcon={
              <Feather name="arrow-right" size={18} color={theme.colors.textOnAccent} />
            }
          />

          <Text variant="caption" tone="muted">
            Ao continuar, você concorda com os termos de uso e com a política de privacidade.
          </Text>

          <View style={{ height: 1, backgroundColor: theme.colors.hairline }} />

          <View style={{ gap: theme.space[4] }}>
            <Text variant="captionBody" tone="muted">
              Primeira vez por aqui?
            </Text>
            <Text variant="captionBody">
              Seu acesso é criado automaticamente ao informar o e-mail — sem cadastro
              separado.
            </Text>
          </View>

          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
            <Feather name="shield" size={14} color={theme.colors.textMuted} />
            <Text variant="caption" tone="muted">
              Seus dados estão seguros conosco.
            </Text>
          </View>
        </View>
      </View>
    </AuthCard>
  );
}
