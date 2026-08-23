import { describeError, describeFailure, type Failure } from '@congrega/api-client/errors';
import {
  attachFiscalDocument,
  confirmGivingEntry,
  getFiscalDocumentFile,
  getGivingEntry,
  TIPOS_DE_DOCUMENTO,
  type FiscalDocumentType,
  type GivingEntryDetail,
} from '@congrega/api-client/giving';
import { formatDate } from '@congrega/core/datetime';
import { cents, formatBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { useCarregamentoGlobal } from '@congrega/ui/GlobalLoading';
import { Chip } from '@congrega/ui/Chip';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Platform, ScrollView, View, useWindowDimensions } from 'react-native';
import { apiClient } from '../../../src/api';
import { type ArquivoEscolhido } from '../../../src/arquivoEmBase64';
import { ROTULO_DA_FREQUENCIA, ROTULO_DO_METODO } from '../../../src/rotulosFinanceiros';
import { SeletorDeArquivo, formatarTamanho } from '../../../src/SeletorDeArquivo';

/** Abaixo disto a ficha e o comprovante deixam de dividir a linha. */
const LARGURA_PARA_DUAS_COLUNAS = 900;

const ROTULO_DO_DOCUMENTO: Record<FiscalDocumentType, string> = {
  NotaFiscal: 'Nota fiscal (NF-e)',
  NotaDeServico: 'Nota de serviço (NFS-e)',
  CupomFiscal: 'Cupom fiscal (CF-e/SAT)',
  Recibo: 'Recibo',
};

/**
 * Devolve a pontuação ao CPF/CNPJ na hora de exibir.
 *
 * O banco guarda só dígitos — com pontuação, "12.345.678/0001-90" e
 * "12345678000190" seriam dois emissores diferentes no mesmo relatório. Mas
 * quatorze dígitos seguidos são ilegíveis para quem confere uma nota na mão, e
 * a formatação é trabalho de quem exibe.
 */
function formatarCpfCnpj(digitos: string): string {
  if (digitos.length === 11) {
    return `${digitos.slice(0, 3)}.${digitos.slice(3, 6)}.${digitos.slice(6, 9)}-${digitos.slice(9)}`;
  }

  if (digitos.length === 14) {
    return (
      `${digitos.slice(0, 2)}.${digitos.slice(2, 5)}.${digitos.slice(5, 8)}` +
      `/${digitos.slice(8, 12)}-${digitos.slice(12)}`
    );
  }

  return digitos;
}

/** A chave da NF-e em grupos de quatro — é como ela vem impressa no DANFE. */
function formatarChave(digitos: string): string {
  return digitos.replace(/(\d{4})(?=\d)/gu, '$1 ');
}

/** Uma linha "rótulo · valor" da ficha. */
function Linha({ rotulo, valor }: { readonly rotulo: string; readonly valor: string }) {
  const theme = useTheme();

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'flex-start',
        gap: theme.space[16],
        paddingVertical: theme.space[8],
        borderBottomWidth: 1,
        borderBottomColor: theme.colors.hairline,
      }}
    >
      <Text variant="captionBody" tone="muted" style={{ flex: 1, minWidth: 0 }}>
        {rotulo}
      </Text>
      <Text variant="bodyStrong" style={{ flex: 2, minWidth: 0, textAlign: 'right' }}>
        {valor}
      </Text>
    </View>
  );
}

export default function DetalheDoLancamento() {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const { id, avisoDoDocumento } = useLocalSearchParams<{
    id: string;
    avisoDoDocumento?: string;
  }>();

  const [lancamento, setLancamento] = useState<GivingEntryDetail | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<Failure | null>(null);

  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  const carregar = useCallback(() => {
    setCarregando(true);
    setErro(null);

    getGivingEntry(apiClient, id)
      .then((resposta) => {
        setLancamento(resposta);
        setCarregando(false);
      })
      .catch((causa: unknown) => {
        setErro(describeFailure(causa));
        setCarregando(false);
      });
  }, [id]);

  useEffect(carregar, [carregar]);

  return (
    <Screen wide>
      <ScrollView
        contentContainerStyle={{
          paddingBottom: theme.space[48],
          gap: theme.space[16],
          maxWidth: 1080,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            FINANCEIRO
          </Text>
          <Text variant="heading">Detalhe do lançamento</Text>
        </View>

        {/* O aviso chega quando o lançamento foi salvo e o comprovante não —
            e é por isso que ele fala em "o lançamento está salvo": sem essa
            frase, o tesoureiro relança a despesa e o caixa conta em dobro. */}
        {typeof avisoDoDocumento === 'string' && avisoDoDocumento !== '' && (
          <View
            style={{
              padding: theme.space[12],
              borderRadius: theme.radius.smallCards,
              borderWidth: 1,
              borderColor: theme.colors.danger,
              gap: theme.space[4],
            }}
          >
            <Text variant="bodyStrong">O lançamento foi salvo, mas o documento não.</Text>
            <Text variant="captionBody">{avisoDoDocumento}</Text>
            <Text variant="captionBody" tone="muted">
              Não lance a despesa de novo — anexe o documento aqui embaixo.
            </Text>
          </View>
        )}

        <AsyncContent
          loading={carregando}
          failure={erro}
          errorTitle="Não deu para carregar o lançamento"
          onRetry={carregar}
          isEmpty={!carregando && erro === null && lancamento === null}
          empty={
            <EmptyState
              title="Lançamento não encontrado"
              description="Ele pode ter sido apagado por outra pessoa."
              action={<Button label="Voltar ao caixa" onPress={() => router.replace('/financeiro')} />}
            />
          }
        >
          {lancamento !== null && (
            <View
              style={{
                flexDirection: emDuasColunas ? 'row' : 'column',
                gap: theme.space[16],
                alignItems: 'flex-start',
              }}
            >
              <View style={{ flex: emDuasColunas ? 1.3 : undefined, width: '100%', minWidth: 0 }}>
                <FichaDoLancamento lancamento={lancamento} aoConfirmar={carregar} />
              </View>

              <View style={{ flex: emDuasColunas ? 1 : undefined, width: '100%', minWidth: 0 }}>
                <PainelDoComprovante lancamento={lancamento} aoMudar={carregar} />
              </View>
            </View>
          )}
        </AsyncContent>

        <Button label="Voltar" variant="ghost" onPress={() => router.back()} />
      </ScrollView>
    </Screen>
  );
}

function FichaDoLancamento({
  lancamento,
  aoConfirmar,
}: {
  readonly lancamento: GivingEntryDetail;
  readonly aoConfirmar: () => void;
}) {
  const theme = useTheme();
  const ehSaida = lancamento.kind === 'Saida';
  const [confirmando, setConfirmando] = useState(false);
  const [erroDaConfirmacao, setErroDaConfirmacao] = useState<string | null>(null);
  const { executar } = useCarregamentoGlobal();

  // Comparação de texto ISO, que ordena igual à cronologia. Passar por `Date`
  // leria `2026-09-30` como meia-noite UTC e, em São Paulo, diria que o dia 30
  // ainda não chegou às 21h do próprio dia 30.
  const hojeIso = new Date().toLocaleDateString('sv-SE');
  const podeConfirmar = lancamento.status === 'Previsto' && lancamento.occurredOn <= hojeIso;

  async function confirmar() {
    setErroDaConfirmacao(null);
    setConfirmando(true);

    try {
      await executar('Confirmando…', 'Marcando o lançamento como realizado.', () =>
        confirmGivingEntry(apiClient, lancamento.id),
      );
      aoConfirmar();
    } catch (causa) {
      setErroDaConfirmacao(describeError(causa));
    } finally {
      setConfirmando(false);
    }
  }

  return (
    <Card style={{ gap: theme.space[16] }}>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12], minWidth: 0 }}>
        <View
          style={{
            width: 44,
            height: 44,
            borderRadius: theme.radius.smallCards,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: ehSaida ? theme.colors.surfaceInner : theme.colors.surfaceAccentSoft,
          }}
        >
          <Feather
            name={ehSaida ? 'arrow-down' : 'arrow-up'}
            size={20}
            color={ehSaida ? theme.colors.danger : theme.colors.textOnAccentSoft}
          />
        </View>

        <View style={{ flex: 1, minWidth: 0 }}>
          <Text variant="subheading" numberOfLines={2}>
            {lancamento.description ?? lancamento.categoryName}
          </Text>
          {/* O tipo vem escrito, e não só pela cor do valor: verde e vermelho
              têm luminância quase igual, e quem não distingue matiz não teria
              como saber se a linha soma ou subtrai. */}
          <Text variant="captionBody" tone="muted">
            {ehSaida ? 'Saída' : 'Entrada'} · {lancamento.categoryName}
          </Text>
        </View>
      </View>

      <Text
        variant="headingLg"
        style={{ color: ehSaida ? theme.colors.danger : theme.colors.textOnAccentSoft }}
      >
        {ehSaida ? '− ' : '+ '}
        {formatBRL(cents(lancamento.amountCents))}
      </Text>

      {lancamento.status === 'Previsto' && (
        <View style={{ gap: theme.space[8] }}>
          <View style={{ flexDirection: 'row' }}>
            <EyebrowPill label="Previsto" tone="badge" />
          </View>

          <Text variant="captionBody" tone="muted">
            {podeConfirmar
              ? 'Ainda não entrou no saldo do mês. Confirme quando o dinheiro se mover.'
              : 'Ainda não entrou no saldo do mês — a data não chegou.'}
          </Text>

          {/* O botão só existe quando a confirmação pode dar certo: o domínio
              recusa confirmar data futura, e oferecer a ação mesmo assim
              produziria um erro que a própria tela sugeriu. */}
          {podeConfirmar && (
            <Button
              label={confirmando ? 'Confirmando…' : 'Confirmar pagamento'}
              onPress={() => void confirmar()}
              disabled={confirmando}
            />
          )}

          {erroDaConfirmacao !== null && (
            <Text variant="captionBody" style={{ color: theme.colors.danger }}>
              {erroDaConfirmacao}
            </Text>
          )}
        </View>
      )}

      <View>
        <Linha rotulo="Data" valor={formatDate(lancamento.occurredOn)} />
        <Linha rotulo="Categoria" valor={lancamento.categoryName} />
        <Linha
          rotulo="Forma de pagamento"
          valor={ROTULO_DO_METODO[lancamento.method] ?? lancamento.method}
        />
        <Linha rotulo="Conta" valor={lancamento.accountName ?? 'Não informada'} />
        <Linha rotulo="Membro" valor={lancamento.memberName ?? 'Sem doador identificado'} />
        <Linha
          rotulo="Repetição"
          valor={
            lancamento.recurrence === null
              ? 'Não se repete'
              : ROTULO_DA_FREQUENCIA[lancamento.recurrence]
          }
        />
        <Linha
          rotulo="Situação"
          valor={lancamento.status === 'Previsto' ? 'Previsto' : 'Realizado'}
        />
        <Linha rotulo="Lançado por" valor={lancamento.recordedByName ?? 'Não registrado'} />
        <Linha rotulo="Registrado em" valor={formatDate(lancamento.createdAt)} />
      </View>

      {lancamento.notes !== null && lancamento.notes !== '' && (
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            OBSERVAÇÃO
          </Text>
          <Text variant="body">{lancamento.notes}</Text>
        </View>
      )}
    </Card>
  );
}

/**
 * O comprovante: mostra o que existe, ou permite anexar o que falta.
 *
 * <b>Em entrada, este painel não aparece.</b> A igreja não emite nota ao receber
 * um dízimo; um formulário de nota fiscal ali seria uma pergunta sem resposta.
 */
function PainelDoComprovante({
  lancamento,
  aoMudar,
}: {
  readonly lancamento: GivingEntryDetail;
  readonly aoMudar: () => void;
}) {
  if (lancamento.kind !== 'Saida') {
    return null;
  }

  return lancamento.document === null ? (
    <FormularioDeComprovante lancamento={lancamento} aoMudar={aoMudar} />
  ) : (
    <ComprovanteExistente lancamento={lancamento} />
  );
}

function ComprovanteExistente({ lancamento }: { readonly lancamento: GivingEntryDetail }) {
  const theme = useTheme();
  const [baixando, setBaixando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const documento = lancamento.document;
  if (documento === null) return null;

  /**
   * Abre o comprovante numa aba.
   *
   * O arquivo vem em Base64 e vira um `blob:` — e não uma URI `data:`. Chrome
   * bloqueia navegação de topo para `data:`, então uma aba aberta com ela
   * simplesmente não carrega. O `blob:` não tem essa restrição.
   */
  async function abrir() {
    setErro(null);
    setBaixando(true);

    try {
      const arquivo = await getFiscalDocumentFile(apiClient, lancamento.id);

      const binario = atob(arquivo.contentBase64);
      const bytes = new Uint8Array(binario.length);
      for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i);

      const url = URL.createObjectURL(new Blob([bytes], { type: arquivo.contentType }));
      globalThis.open(url, '_blank', 'noopener');

      // Revogar imediatamente cancelaria a aba que acabou de abrir; um minuto é
      // muito mais do que o navegador precisa para buscar o blob.
      globalThis.setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setBaixando(false);
    }
  }

  return (
    <Card style={{ gap: theme.space[12] }}>
      <View style={{ gap: theme.space[4] }}>
        <Text variant="eyebrow" tone="muted">
          DOCUMENTO FISCAL
        </Text>
        <Text variant="subheading">{ROTULO_DO_DOCUMENTO[documento.documentType]}</Text>
      </View>

      <View>
        {documento.number !== null && <Linha rotulo="Número" valor={documento.number} />}
        {documento.series !== null && <Linha rotulo="Série" valor={documento.series} />}
        {documento.issuerTaxId !== null && (
          <Linha rotulo="Emissor" valor={formatarCpfCnpj(documento.issuerTaxId)} />
        )}
        {documento.accessKey !== null && (
          <Linha rotulo="Chave de acesso" valor={formatarChave(documento.accessKey)} />
        )}
      </View>

      {documento.file === null ? (
        <Text variant="captionBody" tone="muted">
          O documento foi registrado sem arquivo anexado.
        </Text>
      ) : (
        <View style={{ gap: theme.space[8] }}>
          <View
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              gap: theme.space[12],
              paddingVertical: theme.space[12],
              paddingHorizontal: theme.space[16],
              borderRadius: theme.radius.smallCards,
              borderWidth: 1,
              borderColor: theme.colors.hairline,
              backgroundColor: theme.colors.surfaceInner,
              minWidth: 0,
            }}
          >
            <Feather
              name={documento.file.contentType === 'application/pdf' ? 'file-text' : 'image'}
              size={18}
              color={theme.colors.textOnAccentSoft}
            />
            <View style={{ flex: 1, minWidth: 0 }}>
              <Text variant="bodyStrong" numberOfLines={1}>
                {documento.file.fileName}
              </Text>
              <Text variant="captionBody" tone="muted">
                {formatarTamanho(documento.file.sizeBytes)}
              </Text>
            </View>
          </View>

          {/* Só no navegador: abrir arquivo em iOS/Android exige outro fluxo, e
              um botão que não faz nada ensina a desconfiar dos outros. */}
          {Platform.OS === 'web' && (
            <Button
              label={baixando ? 'Abrindo…' : 'Ver comprovante'}
              variant="ghost"
              onPress={() => void abrir()}
              disabled={baixando}
            />
          )}

          {erro !== null && (
            <Text variant="captionBody" style={{ color: theme.colors.danger }}>
              {erro}
            </Text>
          )}
        </View>
      )}
    </Card>
  );
}

function FormularioDeComprovante({
  lancamento,
  aoMudar,
}: {
  readonly lancamento: GivingEntryDetail;
  readonly aoMudar: () => void;
}) {
  const theme = useTheme();
  const [tipo, setTipo] = useState<FiscalDocumentType>('NotaFiscal');
  const [anexo, setAnexo] = useState<ArquivoEscolhido | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const { executar } = useCarregamentoGlobal();

  const numero = useRef('');
  const serie = useRef('');
  const emissor = useRef('');
  const chave = useRef('');

  async function salvar() {
    if (tipo !== 'Recibo' && numero.current.trim() === '') {
      setErro('Informe o número do documento. Só recibo pode não ter número.');
      return;
    }

    setErro(null);
    setSalvando(true);

    try {
      await executar(
        'Anexando o comprovante…',
        'Enviando o documento fiscal. Arquivos grandes levam alguns segundos.',
        () =>
          attachFiscalDocument(apiClient, lancamento.id, {
        documentType: tipo,
        ...(numero.current.trim() ? { number: numero.current.trim() } : {}),
        ...(serie.current.trim() ? { series: serie.current.trim() } : {}),
        ...(emissor.current.trim() ? { issuerTaxId: emissor.current.trim() } : {}),
        ...(chave.current.trim() ? { accessKey: chave.current.trim() } : {}),
        ...(anexo !== null
          ? {
              attachment: {
                fileName: anexo.fileName,
                contentType: anexo.contentType,
                contentBase64: anexo.contentBase64,
              },
            }
          : {}),
          }),
      );

      aoMudar();
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Card style={{ gap: theme.space[12] }}>
      <View style={{ gap: theme.space[4] }}>
        <Text variant="eyebrow" tone="muted">
          DOCUMENTO FISCAL
        </Text>
        <Text variant="subheading">Sem comprovante ainda</Text>
        {/* A nota costuma chegar depois do pagamento — este painel existe para
            esse caso, e não porque alguém esqueceu de preencher. */}
        <Text variant="captionBody" tone="muted">
          A nota da despesa costuma chegar dias depois do pagamento. Quando ela chegar, anexe aqui.
        </Text>
      </View>

      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          TIPO DE DOCUMENTO
        </Text>
        <View
          style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
          accessibilityRole="radiogroup"
        >
          {TIPOS_DE_DOCUMENTO.map((opcao) => (
            <Chip
              key={opcao}
              label={ROTULO_DO_DOCUMENTO[opcao]}
              selected={tipo === opcao}
              onPress={() => setTipo(opcao)}
            />
          ))}
        </View>
      </View>

      <TextField
        label="Número do documento"
        placeholder="Ex.: 000123"
        defaultValue=""
        onValueChange={(v) => {
          numero.current = v;
        }}
        {...(tipo === 'Recibo' ? { hint: 'Opcional em recibo.' } : {})}
      />

      <TextField
        label="Série"
        placeholder="Ex.: 1"
        defaultValue=""
        onValueChange={(v) => {
          serie.current = v;
        }}
        hint="Opcional."
      />

      <TextField
        label="CNPJ ou CPF do emissor"
        placeholder="00.000.000/0000-00"
        defaultValue=""
        onValueChange={(v) => {
          emissor.current = v;
        }}
        keyboardType="number-pad"
        hint="Opcional. O dígito verificador é conferido."
      />

      <TextField
        label="Chave de acesso"
        placeholder="44 dígitos da NF-e"
        defaultValue=""
        onValueChange={(v) => {
          chave.current = v;
        }}
        keyboardType="number-pad"
        hint="Opcional."
      />

      <View style={{ gap: theme.space[8] }}>
        <Text variant="eyebrow" tone="muted">
          ANEXO DO COMPROVANTE
        </Text>
        <SeletorDeArquivo arquivo={anexo} onEscolher={setAnexo} onRemover={() => setAnexo(null)} />
      </View>

      {erro !== null && (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {erro}
        </Text>
      )}

      <SignatureButton
        label={salvando ? 'Anexando' : 'Anexar documento'}
        onPress={() => void salvar()}
        loading={salvando}
      />
    </Card>
  );
}
