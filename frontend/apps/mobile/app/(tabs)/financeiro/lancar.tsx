import { describeError } from '@congrega/api-client/errors';
import { createGivingEntry, type GivingCategory, type GivingMethod } from '@congrega/api-client/giving';
import { cents, formatBRL, parseBRL } from '@congrega/core/money';
import { Button } from '@congrega/ui/Button';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router } from 'expo-router';
import { useRef, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { SeletorDeMembro, type MembroSelecionado } from '../../../src/SeletorDeMembro';
import { useGivingCategories } from '../../../src/useGiving';

const METODOS: readonly GivingMethod[] = [
  'Dinheiro',
  'Pix',
  'Cartao',
  'Transferencia',
  'Cheque',
  'Outro',
];

const ROTULO_DO_METODO: Record<GivingMethod, string> = {
  Dinheiro: 'Dinheiro',
  Pix: 'Pix',
  Cartao: 'Cartão',
  Transferencia: 'Transferência',
  Cheque: 'Cheque',
  Outro: 'Outro',
};

/** Aceita `31/12/2026` e devolve `2026-12-31`, que é o formato do contrato. */
function paraIso(dataBr: string): string | undefined {
  const partes = /^(\d{2})\/(\d{2})\/(\d{4})$/u.exec(dataBr.trim());
  if (partes === null) return undefined;

  const [, dia, mes, ano] = partes;
  return `${ano}-${mes}-${dia}`;
}

function mascaraData(valor: string): string {
  const d = valor.replace(/\D/gu, '').slice(0, 8);
  if (d.length <= 2) return d;
  if (d.length <= 4) return `${d.slice(0, 2)}/${d.slice(2)}`;
  return `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`;
}

/** Hoje em `dd/mm/aaaa`, para pré-preencher o campo de data. */
function hojeBr(): string {
  const agora = new Date();
  const dia = String(agora.getDate()).padStart(2, '0');
  const mes = String(agora.getMonth() + 1).padStart(2, '0');
  return `${dia}/${mes}/${agora.getFullYear()}`;
}

export default function LancarMovimento() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { categorias, carregando: carregandoCategorias } = useGivingCategories();

  const valor = useRef('');
  const data = useRef(hojeBr());
  const observacao = useRef('');

  const [categoriaId, setCategoriaId] = useState<string | null>(null);
  const [membro, setMembro] = useState<MembroSelecionado | null>(null);
  const [metodo, setMetodo] = useState<GivingMethod>('Dinheiro');
  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    const problemas: Record<string, string> = {};

    if (categoriaId === null) {
      problemas['categoria'] = 'Escolha uma categoria.';
    }

    const valorCents = parseBRL(valor.current);
    if (valorCents === null || valorCents <= 0) {
      problemas['valor'] = 'Informe um valor maior que zero.';
    }

    const dataIso = paraIso(data.current);
    if (dataIso === undefined) {
      problemas['data'] = 'Use o formato dia/mês/ano.';
    }

    setErros(problemas);
    if (Object.keys(problemas).length > 0) return;

    setErroGeral(null);
    setSalvando(true);

    try {
      await createGivingEntry(apiClient, {
        categoryId: categoriaId!,
        amountCents: valorCents!,
        occurredOn: dataIso!,
        method: metodo,
        ...(membro !== null ? { memberId: membro.id } : {}),
        ...(observacao.current.trim() ? { notes: observacao.current.trim() } : {}),
      });

      // `replace` e não `push`: voltar ao formulário depois de salvar convidaria
      // a lançar o mesmo dízimo duas vezes — e no caixa isso é dinheiro que não
      // existe.
      router.replace('/financeiro');
    } catch (causa) {
      setErroGeral(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  if (carregandoCategorias) {
    return (
      <Screen wide style={{ alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color={theme.colors.text} />
      </Screen>
    );
  }

  if (categorias.length === 0) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Nenhuma categoria ainda"
            description="Um lançamento precisa de categoria — é ela que diz se o dinheiro entrou ou saiu. Cadastre a primeira."
            action={
              <SignatureButton
                label="Cadastrar categoria"
                onPress={() => router.replace('/financeiro/categorias')}
              />
            }
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
              CAIXA
            </Text>
            <Text variant="heading">Novo lançamento</Text>
          </View>

          <View style={{ gap: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              CATEGORIA *
            </Text>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
              {categorias.map((categoria: GivingCategory) => (
                <Chip
                  key={categoria.id}
                  label={categoria.name}
                  {...(categoria.kind === 'Saida' ? { sufixo: 'saída' } : {})}
                  selected={categoriaId === categoria.id}
                  onPress={() => {
                    setCategoriaId(categoria.id);
                    if (erros['categoria']) setErros((e) => ({ ...e, categoria: '' }));
                  }}
                />
              ))}
            </View>
            {erros['categoria'] ? (
              <Text variant="captionBody" style={{ color: theme.colors.danger }}>
                {erros['categoria']}
              </Text>
            ) : null}
          </View>

          <TextField
            label="Valor"
            placeholder="0,00"
            defaultValue=""
            onValueChange={(v) => {
              valor.current = v;
              if (erros['valor']) setErros((e) => ({ ...e, valor: '' }));
            }}
            {...(erros['valor'] ? { error: erros['valor'] } : {})}
            keyboardType="decimal-pad"
            inputStyle={{ textAlign: 'right' }}
            hint="Em reais — ex.: 1.250,00"
            autoFocus
          />

          <TextField
            label="Data"
            placeholder="dia/mês/ano"
            defaultValue={hojeBr()}
            transform={mascaraData}
            onValueChange={(v) => {
              data.current = v;
              if (erros['data']) setErros((e) => ({ ...e, data: '' }));
            }}
            {...(erros['data'] ? { error: erros['data'] } : {})}
            keyboardType="number-pad"
          />

          <View style={{ gap: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              FORMA
            </Text>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
              {METODOS.map((opcao) => (
                <Chip
                  key={opcao}
                  label={ROTULO_DO_METODO[opcao]}
                  selected={metodo === opcao}
                  onPress={() => setMetodo(opcao)}
                />
              ))}
            </View>
          </View>

          <SeletorDeMembro
            label="Membro"
            selecionado={membro}
            onSelecionar={setMembro}
            hint="Opcional — deixe em branco para oferta sem doador identificado"
          />

          <TextField
            label="Observação"
            placeholder="opcional"
            defaultValue=""
            onValueChange={(v) => {
              observacao.current = v;
            }}
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
            label={salvando ? 'Salvando' : 'Salvar lançamento'}
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
