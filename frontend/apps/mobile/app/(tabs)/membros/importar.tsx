import { describeError } from '@congrega/api-client/errors';
import { importMembers, type ImportMemberRow, type ImportMembersResult } from '@congrega/api-client/members';
import { parseCsv } from '@congrega/core/csv';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import * as DocumentPicker from 'expo-document-picker';
import { router } from 'expo-router';
import { useMemo, useState } from 'react';
import { Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';

/** Teto do lote — espelha `MemberEndpoints.MaxImportRows` no backend. */
const MAX_ROWS = 500;

interface CampoAlvo {
  readonly chave: 'fullName' | 'email' | 'phone' | 'birthDate' | 'addressCity';
  readonly rotulo: string;
  readonly obrigatorio: boolean;
}

const CAMPOS: readonly CampoAlvo[] = [
  { chave: 'fullName', rotulo: 'Nome completo', obrigatorio: true },
  { chave: 'email', rotulo: 'E-mail', obrigatorio: false },
  { chave: 'phone', rotulo: 'Telefone', obrigatorio: false },
  { chave: 'birthDate', rotulo: 'Data de nascimento', obrigatorio: false },
  { chave: 'addressCity', rotulo: 'Cidade', obrigatorio: false },
];

type Mapeamento = Record<CampoAlvo['chave'], string | null>;

const MAPEAMENTO_VAZIO: Mapeamento = {
  fullName: null,
  email: null,
  phone: null,
  birthDate: null,
  addressCity: null,
};

/** Só dígitos — o backend guarda telefone sem formatação. */
function apenasDigitos(valor: string): string {
  return valor.replace(/\D/gu, '');
}

/** Aceita `31/12/1980` ou já `1980-12-31`; qualquer outra coisa vira "sem data". */
function paraDataIso(valor: string): string | undefined {
  const texto = valor.trim();
  if (texto.length === 0) return undefined;

  if (/^\d{4}-\d{2}-\d{2}$/u.test(texto)) return texto;

  const partesBr = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/u.exec(texto);
  if (partesBr !== null) {
    const [, dia, mes, ano] = partesBr;
    return `${ano}-${mes!.padStart(2, '0')}-${dia!.padStart(2, '0')}`;
  }

  return undefined;
}

type Etapa = 'escolher' | 'mapear' | 'resultado';

export default function ImportarMembros() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();

  const [etapa, setEtapa] = useState<Etapa>('escolher');
  const [nomeArquivo, setNomeArquivo] = useState<string | null>(null);
  const [csv, setCsv] = useState<{ headers: readonly string[]; rows: readonly (readonly string[])[] } | null>(
    null,
  );
  const [mapeamento, setMapeamento] = useState<Mapeamento>(MAPEAMENTO_VAZIO);
  const [erro, setErro] = useState<string | null>(null);
  const [carregandoArquivo, setCarregandoArquivo] = useState(false);
  const [importando, setImportando] = useState(false);
  const [resultado, setResultado] = useState<ImportMembersResult | null>(null);

  async function escolherArquivo() {
    setErro(null);
    setCarregandoArquivo(true);

    try {
      const escolhido = await DocumentPicker.getDocumentAsync({
        type: ['text/csv', 'text/comma-separated-values', 'text/plain'],
        copyToCacheDirectory: true,
      });

      if (escolhido.canceled || escolhido.assets.length === 0) {
        return;
      }

      const arquivo = escolhido.assets[0]!;
      const texto = await (await fetch(arquivo.uri)).text();
      const analisado = parseCsv(texto);

      if (analisado.headers.length === 0 || analisado.rows.length === 0) {
        setErro('Não encontramos colunas ou linhas nesse arquivo. Confira se é um CSV válido.');
        return;
      }

      if (analisado.rows.length > MAX_ROWS) {
        setErro(
          `Essa planilha tem ${analisado.rows.length} linhas. Envie no máximo ${MAX_ROWS} por vez — divida em arquivos menores.`,
        );
        return;
      }

      setNomeArquivo(arquivo.name);
      setCsv(analisado);

      // Tenta casar automaticamente pelo nome da coluna — poupa clique de quem
      // já exportou com cabeçalhos óbvios ("nome", "e-mail"...).
      setMapeamento(sugerirMapeamento(analisado.headers));
      setEtapa('mapear');
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setCarregandoArquivo(false);
    }
  }

  const linhasMapeadas = useMemo<readonly ImportMemberRow[]>(() => {
    if (csv === null || mapeamento.fullName === null) return [];

    const indice = (cabecalho: string | null): number =>
      cabecalho === null ? -1 : csv.headers.indexOf(cabecalho);

    const idxNome = indice(mapeamento.fullName);
    const idxEmail = indice(mapeamento.email);
    const idxTelefone = indice(mapeamento.phone);
    const idxNascimento = indice(mapeamento.birthDate);
    const idxCidade = indice(mapeamento.addressCity);

    return csv.rows.map((linha) => {
      const nome = (idxNome >= 0 ? linha[idxNome] : '') ?? '';
      const email = idxEmail >= 0 ? linha[idxEmail]?.trim() : undefined;
      const telefoneBruto = idxTelefone >= 0 ? linha[idxTelefone] : undefined;
      const nascimentoBruto = idxNascimento >= 0 ? linha[idxNascimento] : undefined;
      const cidade = idxCidade >= 0 ? linha[idxCidade]?.trim() : undefined;

      const telefone = telefoneBruto ? apenasDigitos(telefoneBruto) : '';
      const nascimento = nascimentoBruto ? paraDataIso(nascimentoBruto) : undefined;

      return {
        fullName: nome.trim(),
        ...(email ? { email } : {}),
        ...(telefone ? { phone: telefone } : {}),
        ...(nascimento ? { birthDate: nascimento } : {}),
        ...(cidade ? { addressCity: cidade } : {}),
      };
    });
  }, [csv, mapeamento]);

  async function importar() {
    if (linhasMapeadas.length === 0) return;

    setErro(null);
    setImportando(true);

    try {
      const resposta = await importMembers(apiClient, linhasMapeadas);
      setResultado(resposta);
      setEtapa('resultado');
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setImportando(false);
    }
  }

  function recomecar() {
    setEtapa('escolher');
    setNomeArquivo(null);
    setCsv(null);
    setMapeamento(MAPEAMENTO_VAZIO);
    setResultado(null);
    setErro(null);
  }

  const voltar = (
    <Pressable
      onPress={() => (etapa === 'escolher' ? router.back() : recomecar())}
      accessibilityRole="button"
      accessibilityLabel="Voltar"
      hitSlop={8}
      style={({ pressed }) => ({
        width: theme.touch.minTarget,
        height: theme.touch.minTarget,
        alignItems: 'flex-start',
        justifyContent: 'center',
        opacity: pressed ? 0.6 : 1,
      })}
    >
      <Feather name="chevron-left" size={26} color={theme.colors.text} />
    </Pressable>
  );

  return (
    <Screen padded={false} wide>
      <ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[8],
          paddingHorizontal: theme.space[24],
          paddingBottom: insets.bottom + theme.space[48],
          gap: theme.space[20],
          maxWidth: 640,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        {voltar}

        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            SUA IGREJA
          </Text>
          <Text variant="heading">Importar membros</Text>
        </View>

        {erro !== null && (
          <View
            style={{
              padding: theme.space[12],
              borderRadius: theme.radius.inputs,
              borderWidth: 1,
              borderColor: theme.colors.danger,
            }}
          >
            <Text variant="captionBody">{erro}</Text>
          </View>
        )}

        {etapa === 'escolher' && (
          <View style={{ gap: theme.space[16] }}>
            <Text variant="body" tone="muted">
              Suba um arquivo CSV com a lista de membros. Na próxima tela você escolhe qual
              coluna da sua planilha é o nome, o e-mail, e assim por diante.
            </Text>
            <SignatureButton
              label={carregandoArquivo ? 'Lendo arquivo' : 'Selecionar arquivo CSV'}
              onPress={() => void escolherArquivo()}
              loading={carregandoArquivo}
            />
          </View>
        )}

        {etapa === 'mapear' && csv !== null && (
          <View style={{ gap: theme.space[20] }}>
            <Text variant="captionBody" tone="muted">
              {nomeArquivo} · {csv.rows.length} {csv.rows.length === 1 ? 'linha' : 'linhas'}
            </Text>

            {CAMPOS.map((campo) => (
              <View key={campo.chave} style={{ gap: theme.space[8] }}>
                <Text variant="eyebrow" tone="muted">
                  {campo.rotulo.toUpperCase()}
                  {campo.obrigatorio ? ' *' : ''}
                </Text>
                <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
                  {!campo.obrigatorio && (
                    <Chip
                      label="Não importar"
                      selected={mapeamento[campo.chave] === null}
                      onPress={() => setMapeamento((m) => ({ ...m, [campo.chave]: null }))}
                    />
                  )}
                  {csv.headers.map((cabecalho) => (
                    <Chip
                      key={cabecalho}
                      label={cabecalho}
                      selected={mapeamento[campo.chave] === cabecalho}
                      onPress={() => setMapeamento((m) => ({ ...m, [campo.chave]: cabecalho }))}
                    />
                  ))}
                </View>
              </View>
            ))}

            {mapeamento.fullName === null && (
              <Text variant="captionBody" tone="muted">
                Escolha qual coluna é o nome completo para continuar.
              </Text>
            )}

            {linhasMapeadas.length > 0 && (
              <Card>
                <Text variant="eyebrow" tone="muted">
                  PRÉVIA
                </Text>
                <View style={{ gap: theme.space[4], marginTop: theme.space[8] }}>
                  {linhasMapeadas.slice(0, 3).map((linha, indice) => (
                    <Text key={indice} variant="captionBody" numberOfLines={1}>
                      {linha.fullName || '(sem nome)'}
                      {linha.email ? ` · ${linha.email}` : ''}
                    </Text>
                  ))}
                  {linhasMapeadas.length > 3 && (
                    <Text variant="captionBody" tone="muted">
                      + {linhasMapeadas.length - 3} linhas
                    </Text>
                  )}
                </View>
              </Card>
            )}

            <SignatureButton
              label={importando ? 'Importando' : `Importar ${linhasMapeadas.length} membros`}
              onPress={() => void importar()}
              loading={importando}
              disabled={mapeamento.fullName === null}
            />
            <Button label="Cancelar" variant="ghost" onPress={recomecar} />
          </View>
        )}

        {etapa === 'resultado' && resultado !== null && (
          <View style={{ gap: theme.space[16] }}>
            <Card>
              <Text variant="subheading">
                {resultado.imported} {resultado.imported === 1 ? 'membro importado' : 'membros importados'}
              </Text>
              {resultado.skipped > 0 && (
                <Text variant="body" tone="muted" style={{ marginTop: theme.space[4] }}>
                  {resultado.skipped} {resultado.skipped === 1 ? 'linha pulada' : 'linhas puladas'}
                </Text>
              )}
            </Card>

            {resultado.issues.length > 0 && (
              <View style={{ gap: theme.space[8] }}>
                <Text variant="eyebrow" tone="muted">
                  LINHAS PULADAS
                </Text>
                {resultado.issues.map((problema) => (
                  <Text key={problema.row} variant="captionBody" tone="muted">
                    Linha {problema.row}: {problema.reason}
                  </Text>
                ))}
              </View>
            )}

            <SignatureButton label="Ver membros" onPress={() => router.replace('/membros')} />
            <Button label="Importar outra planilha" variant="ghost" onPress={recomecar} />
          </View>
        )}

        {etapa === 'escolher' && csv === null && (
          <EmptyState
            title="Nenhum arquivo selecionado ainda"
            description="O CSV pode vir de qualquer planilha — Excel, Google Sheets ou Numbers exportam para esse formato."
          />
        )}
      </ScrollView>
    </Screen>
  );
}

/** Casa cabeçalhos óbvios com o campo correspondente, sem diferenciar acento ou caixa. */
function sugerirMapeamento(headers: readonly string[]): Mapeamento {
  // Remove os sinais diacríticos que a decomposição NFD separou da letra —
  // ficam no intervalo Unicode 0x0300–0x036F ("Combining Diacritical Marks").
  const semAcento = (valor: string): string =>
    Array.from(valor.normalize('NFD'))
      .filter((caractere) => {
        const codigo = caractere.codePointAt(0) ?? 0;
        return codigo < 0x0300 || codigo > 0x036f;
      })
      .join('');

  const normalizar = (valor: string): string => semAcento(valor).toLowerCase().trim();

  const candidatos: Record<CampoAlvo['chave'], readonly string[]> = {
    fullName: ['nome', 'nome completo', 'membro', 'full name'],
    email: ['email', 'e-mail'],
    phone: ['telefone', 'celular', 'fone', 'whatsapp'],
    birthDate: ['nascimento', 'data de nascimento', 'aniversario', 'birth date'],
    addressCity: ['cidade', 'city'],
  };

  const normalizados = headers.map((h) => ({ original: h, normalizado: normalizar(h) }));
  const mapeamento = { ...MAPEAMENTO_VAZIO };

  for (const campo of CAMPOS) {
    const encontrado = normalizados.find((h) => candidatos[campo.chave].includes(h.normalizado));
    if (encontrado) {
      mapeamento[campo.chave] = encontrado.original;
    }
  }

  return mapeamento;
}
