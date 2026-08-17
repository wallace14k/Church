import { assignMemberFamily, changeMemberStatus, getMember, updateMember, type Member } from '@congrega/api-client/members';
import { createFamily, type Family } from '@congrega/api-client/families';
import { describeError } from '@congrega/api-client/errors';
import { isProbablyEmail } from '@congrega/core/validation';
import { Button } from '@congrega/ui/Button';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { TextLink } from '@congrega/ui/TextLink';
import { useTheme } from '@congrega/ui/theme';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../../src/api';
import { useFamilies } from '../../../../src/useFamilies';

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

/** Inverso de `paraIso`, para preencher o campo com o que já veio da API. */
function paraBr(dataIso: string): string {
  const partes = /^(\d{4})-(\d{2})-(\d{2})/u.exec(dataIso);
  if (partes === null) return '';
  const [, ano, mes, dia] = partes;
  return `${dia}/${mes}/${ano}`;
}

/** Máscara progressiva de data enquanto se digita. */
function mascaraData(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  if (d.length <= 2) return d;
  if (d.length <= 4) return `${d.slice(0, 2)}/${d.slice(2)}`;
  return `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`;
}

export default function EditarMembro() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [membro, setMembro] = useState<Member | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erroCarregar, setErroCarregar] = useState<string | null>(null);

  const nome = useRef('');
  const email = useRef('');
  const telefone = useRef('');
  const nascimento = useRef('');

  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [alternandoStatus, setAlternandoStatus] = useState(false);

  const { familias, recarregar: recarregarFamilias } = useFamilies();
  const [alterandoFamilia, setAlterandoFamilia] = useState(false);
  const [mostrandoNovaFamilia, setMostrandoNovaFamilia] = useState(false);
  const [criandoFamilia, setCriandoFamilia] = useState(false);
  const nomeNovaFamilia = useRef('');

  useEffect(() => {
    let cancelado = false;

    getMember(apiClient, id ?? '')
      .then((encontrado) => {
        if (cancelado) return;
        setMembro(encontrado);
        nome.current = encontrado.fullName;
        email.current = encontrado.email ?? '';
        telefone.current = encontrado.phone ?? '';
        nascimento.current = encontrado.birthDate ? paraBr(encontrado.birthDate) : '';
      })
      .catch((causa) => {
        if (!cancelado) setErroCarregar(describeError(causa));
      })
      .finally(() => {
        if (!cancelado) setCarregando(false);
      });

    return () => {
      cancelado = true;
    };
  }, [id]);

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
      await updateMember(apiClient, id ?? '', {
        fullName: nome.current.trim(),
        ...(email.current.trim() ? { email: email.current.trim() } : {}),
        ...(telefone.current ? { phone: telefone.current } : {}),
        ...(dataIso ? { birthDate: dataIso } : {}),
      });

      router.replace(`/membros/${id}`);
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  async function alternarStatus() {
    if (membro === null) return;

    setAlternandoStatus(true);
    try {
      const novoStatus = membro.status === 'Ativo' ? 'Inativo' : 'Ativo';
      const atualizado = await changeMemberStatus(apiClient, id ?? '', novoStatus);
      setMembro(atualizado);
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setAlternandoStatus(false);
    }
  }

  async function escolherFamilia(familyId: string | null) {
    if (membro === null) return;

    setAlterandoFamilia(true);
    try {
      const atualizado = await assignMemberFamily(apiClient, id ?? '', familyId);
      setMembro(atualizado);
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setAlterandoFamilia(false);
    }
  }

  async function criarEVincularFamilia() {
    const nome = nomeNovaFamilia.current.trim();
    if (nome.length < 2) return;

    setCriandoFamilia(true);
    try {
      const familia = await createFamily(apiClient, nome);
      recarregarFamilias();
      await escolherFamilia(familia.id);
      nomeNovaFamilia.current = '';
      setMostrandoNovaFamilia(false);
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setCriandoFamilia(false);
    }
  }

  if (carregando) {
    return (
      <Screen wide style={{ alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color={theme.colors.text} />
      </Screen>
    );
  }

  if (erroCarregar !== null || membro === null) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Ficha não encontrada"
            description={erroCarregar ?? 'Este membro não existe ou não pertence à sua igreja.'}
            action={<Button label="Voltar" onPress={() => router.back()} />}
          />
        </View>
      </Screen>
    );
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
              EDITAR
            </Text>
            <Text variant="heading">{membro.fullName}</Text>
          </View>

          <TextField
            label="Nome completo"
            placeholder="Maria Aparecida da Silva"
            defaultValue={membro.fullName}
            onValueChange={(v) => {
              nome.current = v;
              if (erros['nome']) setErros((e) => ({ ...e, nome: '' }));
            }}
            {...(erros['nome'] ? { error: erros['nome'] } : {})}
            autoCapitalize="words"
          />

          <TextField
            label="E-mail"
            placeholder="opcional"
            defaultValue={membro.email ?? ''}
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
            defaultValue={membro.phone ?? ''}
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
            defaultValue={membro.birthDate ? paraBr(membro.birthDate) : ''}
            transform={mascaraData}
            onValueChange={(v) => {
              nascimento.current = v;
              if (erros['nascimento']) setErros((e) => ({ ...e, nascimento: '' }));
            }}
            {...(erros['nascimento'] ? { error: erros['nascimento'] } : {})}
            keyboardType="number-pad"
            hint="Usada no relatório de aniversariantes"
          />

          <View style={{ gap: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              FAMÍLIA
            </Text>

            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
              <Chip
                label="Nenhuma"
                selected={membro.familyName === null}
                disabled={alterandoFamilia}
                onPress={() => void escolherFamilia(null)}
              />
              {familias.map((familia: Family) => (
                <Chip
                  key={familia.id}
                  label={familia.name}
                  selected={familia.name === membro.familyName}
                  disabled={alterandoFamilia}
                  onPress={() => void escolherFamilia(familia.id)}
                />
              ))}
            </View>

            {mostrandoNovaFamilia ? (
              <View style={{ flexDirection: 'row', gap: theme.space[8], alignItems: 'flex-end' }}>
                <View style={{ flex: 1 }}>
                  <TextField
                    label="Nova família"
                    placeholder="Família Silva"
                    defaultValue=""
                    onValueChange={(v) => {
                      nomeNovaFamilia.current = v;
                    }}
                    autoCapitalize="words"
                    autoFocus
                  />
                </View>
                <Button
                  label="Criar"
                  loading={criandoFamilia}
                  onPress={() => void criarEVincularFamilia()}
                />
              </View>
            ) : (
              <TextLink label="+ Nova família" onPress={() => setMostrandoNovaFamilia(true)} />
            )}
          </View>

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
            label={salvando ? 'Salvando' : 'Salvar alterações'}
            onPress={() => void salvar()}
            loading={salvando}
            style={{ marginTop: theme.space[8] }}
          />

          <Button label="Cancelar" variant="ghost" onPress={() => router.back()} />

          <View
            style={{
              marginTop: theme.space[16],
              paddingTop: theme.space[16],
              borderTopWidth: 1,
              borderTopColor: theme.colors.hairline,
              gap: theme.space[8],
            }}
          >
            <Text variant="captionBody" tone="muted">
              {membro.status === 'Ativo'
                ? 'Inativar tira o membro das listas e relatórios padrão, sem apagar o cadastro.'
                : 'Ativar devolve o membro às listas e relatórios padrão.'}
            </Text>
            <Button
              label={
                alternandoStatus
                  ? 'Alterando'
                  : membro.status === 'Ativo'
                    ? 'Inativar membro'
                    : 'Reativar membro'
              }
              variant="outline"
              loading={alternandoStatus}
              onPress={() => void alternarStatus()}
            />
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </Screen>
  );
}

