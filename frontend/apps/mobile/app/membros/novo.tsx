import { createMember } from '@congrega/api-client/members';
import { describeError } from '@congrega/api-client/errors';
import { isProbablyEmail } from '@congrega/core/validation';
import { Button } from '@congrega/ui/Button';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../src/api';

/** Só dígitos, no máximo 11 — o backend guarda sem formatação. */
function apenasDigitos(valor: string): string {
  return valor.replace(/\D/gu, '').slice(0, 11);
}

/** Aceita `31/12/1980` e devolve `1980-12-31`, que é o formato do contrato. */
function paraIso(dataBr: string): string | undefined {
  const partes = /^(\d{2})\/(\d{2})\/(\d{4})$/u.exec(dataBr.trim());
  if (partes === null) return undefined;

  const [, dia, mes, ano] = partes;
  return `${ano}-${mes}-${dia}`;
}

/** Máscara progressiva de data enquanto se digita. */
function mascaraData(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  if (d.length <= 2) return d;
  if (d.length <= 4) return `${d.slice(0, 2)}/${d.slice(2)}`;
  return `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`;
}

export default function NovoMembro() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  // Campos em ref, não em estado: os campos são não controlados e o valor só é
  // lido no envio. Um formulário de sete campos em estado re-renderizaria a tela
  // inteira a cada tecla.
  const nome = useRef('');
  const email = useRef('');
  const telefone = useRef('');
  const nascimento = useRef('');
  const cidade = useRef('');

  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    const problemas: Record<string, string> = {};

    if (nome.current.trim().length < 2) {
      problemas['nome'] = 'Informe o nome completo.';
    }

    if (email.current.trim().length > 0 && !isProbablyEmail(email.current)) {
      problemas['email'] = 'Esse e-mail não parece válido.';
    }

    const dataIso = nascimento.current.trim().length > 0 ? paraIso(nascimento.current) : undefined;
    if (nascimento.current.trim().length > 0 && dataIso === undefined) {
      problemas['nascimento'] = 'Use o formato dia/mês/ano.';
    }

    setErros(problemas);
    if (Object.keys(problemas).length > 0) return;

    setErroGeral(null);
    setSalvando(true);

    try {
      await createMember(apiClient, {
        fullName: nome.current.trim(),
        ...(email.current.trim() ? { email: email.current.trim() } : {}),
        ...(telefone.current ? { phone: telefone.current } : {}),
        ...(dataIso ? { birthDate: dataIso } : {}),
        ...(cidade.current.trim() ? { addressCity: cidade.current.trim() } : {}),
      });

      // `replace` e não `push`: voltar para o formulário depois de salvar
      // convidaria a cadastrar a mesma pessoa duas vezes.
      router.replace('/membros');
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Screen padded={false}>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView
          contentContainerStyle={{
            paddingTop: insets.top + theme.space[16],
            paddingHorizontal: theme.space[24],
            paddingBottom: insets.bottom + theme.space[48],
            gap: theme.space[16],
          }}
          keyboardShouldPersistTaps="handled"
        >
          <View style={{ gap: theme.space[4], marginBottom: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              CADASTRO
            </Text>
            <Text variant="heading">Novo membro</Text>
            <Text variant="body" tone="muted">
              Só o nome é obrigatório. O resto pode ser preenchido depois, conforme a secretaria
              tiver a informação.
            </Text>
          </View>

          <TextField
            label="Nome completo"
            placeholder="Maria Aparecida da Silva"
            defaultValue=""
            onValueChange={(v) => {
              nome.current = v;
              if (erros['nome']) setErros((e) => ({ ...e, nome: '' }));
            }}
            {...(erros['nome'] ? { error: erros['nome'] } : {})}
            autoCapitalize="words"
            autoFocus
          />

          <TextField
            label="E-mail"
            placeholder="opcional"
            defaultValue=""
            onValueChange={(v) => {
              email.current = v;
              if (erros['email']) setErros((e) => ({ ...e, email: '' }));
            }}
            {...(erros['email'] ? { error: erros['email'] } : {})}
            keyboardType="email-address"
            autoCapitalize="none"
            autoCorrect={false}
          />

          <TextField
            label="Telefone"
            placeholder="opcional"
            defaultValue=""
            transform={apenasDigitos}
            onValueChange={(v) => {
              telefone.current = v;
            }}
            keyboardType="phone-pad"
            hint="Só números, com DDD"
          />

          <TextField
            label="Data de nascimento"
            placeholder="dia/mês/ano"
            defaultValue=""
            transform={mascaraData}
            onValueChange={(v) => {
              nascimento.current = v;
              if (erros['nascimento']) setErros((e) => ({ ...e, nascimento: '' }));
            }}
            {...(erros['nascimento'] ? { error: erros['nascimento'] } : {})}
            keyboardType="number-pad"
            hint="Usada no relatório de aniversariantes"
          />

          <TextField
            label="Cidade"
            placeholder="opcional"
            defaultValue=""
            onValueChange={(v) => {
              cidade.current = v;
            }}
            autoCapitalize="words"
          />

          {erroGeral !== null && (
            <View
              style={{
                padding: theme.space[12],
                borderRadius: theme.radius.inputs,
                borderWidth: 1,
                borderColor: theme.colors.danger,
              }}
            >
              <Text variant="captionBody">{erroGeral}</Text>
            </View>
          )}

          <SignatureButton
            label={salvando ? 'Salvando' : 'Salvar membro'}
            onPress={() => void salvar()}
            loading={salvando}
            style={{ marginTop: theme.space[8] }}
          />

          <Button label="Cancelar" variant="ghost" onPress={() => router.back()} />
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}
