import { createFamily } from '@congrega/api-client/families';
import { describeError } from '@congrega/api-client/errors';
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
import { apiClient } from '../../../../src/api';

export default function NovaFamilia() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  const nome = useRef('');
  const [erro, setErro] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    if (nome.current.trim().length < 2) {
      setErro('Informe um nome para a família.');
      return;
    }

    setErro(null);
    setSalvando(true);

    try {
      await createFamily(apiClient, nome.current.trim());
      router.replace('/membros/familias');
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Screen padded={false} wide>
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : undefined} style={{ flex: 1 }}>
        <ScrollView
          contentContainerStyle={{
            paddingTop: insets.top + theme.space[16],
            paddingHorizontal: theme.space[24],
            paddingBottom: insets.bottom + theme.space[48],
            gap: theme.space[16],
            maxWidth: 480,
            width: '100%',
            alignSelf: 'center',
          }}
          keyboardShouldPersistTaps="handled"
        >
          <View style={{ gap: theme.space[4], marginBottom: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              CADASTRO
            </Text>
            <Text variant="heading">Nova família</Text>
            <Text variant="body" tone="muted">
              Depois de criada, vincule os membros a ela pela ficha de cada um.
            </Text>
          </View>

          <TextField
            label="Nome da família"
            placeholder="Família Silva"
            defaultValue=""
            onValueChange={(v) => {
              nome.current = v;
              if (erro) setErro(null);
            }}
            {...(erro ? { error: erro } : {})}
            autoCapitalize="words"
            autoFocus
          />

          <SignatureButton
            label={salvando ? 'Salvando' : 'Salvar família'}
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
