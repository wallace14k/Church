import { describeError } from '@congrega/api-client/errors';
import {
  attachFiscalDocument,
  createGivingEntry,
  listFinancialAccounts,
  TIPOS_DE_DOCUMENTO,
  type FiscalDocumentType,
  type FinancialAccount,
  type GivingCategory,
  METODOS_OFERECIDOS,
  type GivingMethod,
  type GivingRecurrence,
} from '@congrega/api-client/giving';
import { cents, formatBRL, parseBRL } from '@congrega/core/money';
import { Button } from '@congrega/ui/Button';
import { Chip } from '@congrega/ui/Chip';
import { useCarregamentoGlobal } from '@congrega/ui/GlobalLoading';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { ScreenLoading } from '@congrega/ui/ScreenLoading';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { router } from 'expo-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { type ArquivoEscolhido } from '../../../src/arquivoEmBase64';
import { SeletorDeArquivo } from '../../../src/SeletorDeArquivo';
import { SeletorDeMembro, type MembroSelecionado } from '../../../src/SeletorDeMembro';
import { ROTULO_DO_METODO } from '../../../src/rotulosFinanceiros';
import { useGivingCategories } from '../../../src/useGiving';


/**
 * Quantas parcelas cada frequência gera no horizonte de 12 meses.
 *
 * Espelha `GivingEntry.HorizonteEmMeses` no servidor, e é **texto de aviso, não
 * regra**: quem calcula de verdade é o domínio. O número exato do semanal varia
 * com o dia de início — "cerca de 52" é menos preciso e mais útil para quem só
 * quer a ordem de grandeza antes de salvar.
 */
const QUANTAS_PARCELAS: Record<GivingRecurrence, string> = {
  Semanal: 'cerca de 52 parcelas',
  Mensal: '12 parcelas',
  Anual: '1 parcela',
};

/** Como cada tipo de documento se chama para quem preenche. */
const ROTULO_DO_DOCUMENTO: Record<FiscalDocumentType, string> = {
  NotaFiscal: 'Nota fiscal (NF-e)',
  NotaDeServico: 'Nota de serviço (NFS-e)',
  CupomFiscal: 'Cupom fiscal (CF-e/SAT)',
  Recibo: 'Recibo',
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
  const { executar } = useCarregamentoGlobal();

  const valor = useRef('');
  const data = useRef(hojeBr());
  const observacao = useRef('');
  const titulo = useRef('');

  const [categoriaId, setCategoriaId] = useState<string | null>(null);

  /**
   * Tipo do lançamento — a informação que passou a viver aqui.
   *
   * Antes o sinal vinha da categoria. Com `Ambos` no conjunto, uma categoria
   * pode não ter sinal para emprestar, e o lançamento precisa dizer o que é.
   *
   * O padrão é `Entrada` porque é o lançamento mais frequente numa igreja: o
   * dízimo e a oferta do culto. Quem lança despesa troca uma vez.
   */
  const [tipo, setTipo] = useState<'Entrada' | 'Saida'>('Entrada');

  const [contaId, setContaId] = useState<string | null>(null);
  const [recorrencia, setRecorrencia] = useState<GivingRecurrence | null>(null);
  const [contas, setContas] = useState<readonly FinancialAccount[]>([]);
  const [membro, setMembro] = useState<MembroSelecionado | null>(null);
  const [metodo, setMetodo] = useState<GivingMethod>('Dinheiro');
  // ------------------------------------------------------------- documento
  //
  // Tudo isto só existe quando o lançamento é saída. Nota fiscal de entrada não
  // existe — a igreja não emite nota ao receber dízimo — e o servidor recusa
  // com 400, pela mesma razão que a FK composta recusa no banco.
  const [temDocumento, setTemDocumento] = useState(false);
  const [tipoDoDocumento, setTipoDoDocumento] = useState<FiscalDocumentType>('NotaFiscal');
  const [anexo, setAnexo] = useState<ArquivoEscolhido | null>(null);
  const numeroDoDocumento = useRef('');
  const serieDoDocumento = useRef('');
  const emissor = useRef('');
  const chaveDeAcesso = useRef('');

  const [erros, setErros] = useState<Record<string, string>>({});
  const [erroGeral, setErroGeral] = useState<string | null>(null);
  const [salvando, setSalvando] = useState(false);

  // As contas são opcionais: se a consulta falhar, o campo simplesmente não
  // aparece e o lançamento continua possível. Barrar o caixa da igreja porque
  // uma lista acessória não carregou seria desproporcional.
  useEffect(() => {
    const controlador = new AbortController();

    listFinancialAccounts(apiClient, false, controlador.signal)
      .then(setContas)
      .catch(() => {});

    return () => controlador.abort();
  }, []);

  /**
   * Categorias que aceitam o tipo escolhido.
   *
   * É a regra que o servidor também aplica: lançar saída em "Dízimo" é recusado
   * com 400. Filtrar aqui evita o usuário escolher para depois levar erro.
   */
  const categoriasCompativeis = useMemo(
    () => categorias.filter((c) => c.kind === 'Ambos' || c.kind === tipo),
    [categorias, tipo],
  );

  // Trocar o tipo pode invalidar a categoria já escolhida — e deixá-la
  // selecionada mandaria ao servidor um par que ele recusa.
  useEffect(() => {
    if (categoriaId !== null && !categoriasCompativeis.some((c) => c.id === categoriaId)) {
      setCategoriaId(null);
    }
  }, [categoriasCompativeis, categoriaId]);

  // Trocar o tipo para Entrada apaga o documento. Deixá-lo preenchido na tela
  // prometeria algo que o servidor recusa — e guardá-lo escondido faria o
  // usuário voltar para Saída e encontrar dados que ele achava que sumiram.
  useEffect(() => {
    if (tipo === 'Entrada' && temDocumento) {
      setTemDocumento(false);
      setAnexo(null);
    }
  }, [tipo, temDocumento]);

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

    // O número é obrigatório em tudo que não é recibo — é o que identifica o
    // documento perante o fisco. Verificar aqui evita mandar o lançamento para
    // depois o anexo falhar.
    if (
      tipo === 'Saida' &&
      temDocumento &&
      tipoDoDocumento !== 'Recibo' &&
      numeroDoDocumento.current.trim() === ''
    ) {
      setErros({ documento: 'Informe o número do documento. Só recibo pode não ter número.' });
      return;
    }

    setErroGeral(null);
    setSalvando(true);

    try {
      const lancamento = await executar(
        recorrencia === null ? 'Lançando…' : 'Criando a série…',
        recorrencia === null
          ? 'Guardando no caixa da igreja.'
          : 'Gerando as parcelas previstas dos próximos 12 meses.',
        () =>
          createGivingEntry(apiClient, {
        categoryId: categoriaId!,
        kind: tipo,
        amountCents: valorCents!,
        occurredOn: dataIso!,
        method: metodo,
        ...(titulo.current.trim() ? { description: titulo.current.trim() } : {}),
        ...(membro !== null ? { memberId: membro.id } : {}),
        ...(contaId !== null ? { accountId: contaId } : {}),
        ...(recorrencia !== null ? { recurrence: recorrencia } : {}),
        ...(observacao.current.trim() ? { notes: observacao.current.trim() } : {}),
          }),
      );

      // O documento vai numa segunda chamada, e a ordem importa: **se ela
      // falhar, o lançamento continua salvo**. É o estado certo — o comprovante
      // é opcional, e perder a despesa por causa do anexo seria pior do que
      // ficar sem o anexo. O usuário é levado ao detalhe, onde pode anexá-lo de
      // novo sem digitar nada outra vez.
      if (tipo === 'Saida' && temDocumento) {
        try {
          await executar(
            'Anexando o comprovante…',
            'Enviando o documento fiscal. Arquivos grandes levam alguns segundos.',
            () =>
              attachFiscalDocument(apiClient, lancamento.id, {
            documentType: tipoDoDocumento,
            ...(numeroDoDocumento.current.trim()
              ? { number: numeroDoDocumento.current.trim() }
              : {}),
            ...(serieDoDocumento.current.trim() ? { series: serieDoDocumento.current.trim() } : {}),
            ...(emissor.current.trim() ? { issuerTaxId: emissor.current.trim() } : {}),
            ...(chaveDeAcesso.current.trim() ? { accessKey: chaveDeAcesso.current.trim() } : {}),
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
        } catch (causaDoDocumento) {
          router.replace({
            pathname: '/financeiro/[id]',
            params: {
              id: lancamento.id,
              avisoDoDocumento: describeError(causaDoDocumento),
            },
          });
          return;
        }
      }

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
      <Screen wide>
        <ScreenLoading what="as categorias" />
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
              TIPO *
            </Text>
            {/* Antes da categoria de propósito: é o tipo que decide quais
                categorias aparecem, e perguntar a categoria primeiro faria a
                lista mudar debaixo de quem acabou de escolher. */}
            <View
              style={{ flexDirection: 'row', gap: theme.space[8] }}
              accessibilityRole="radiogroup"
            >
              <Chip
                label="Entrada"
                selected={tipo === 'Entrada'}
                onPress={() => setTipo('Entrada')}
              />
              <Chip label="Saída" selected={tipo === 'Saida'} onPress={() => setTipo('Saida')} />
            </View>
          </View>

          <View style={{ gap: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              CATEGORIA *
            </Text>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
              {categoriasCompativeis.map((categoria: GivingCategory) => (
                <Chip
                  key={categoria.id}
                  label={categoria.name}
                  {...(categoria.kind === 'Ambos' ? { sufixo: 'entrada ou saída' } : {})}
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
              {METODOS_OFERECIDOS.map((opcao) => (
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
              label="Título"
              placeholder="Aluguel, oferta do culto, energia…"
              defaultValue=""
              onValueChange={(v) => {
                titulo.current = v;
              }}
              autoCapitalize="sentences"
              hint="Opcional. Sem título, a lista mostra o nome da categoria."
            />



          {contas.length > 0 && (
            <View style={{ gap: theme.space[8] }}>
              <Text variant="eyebrow" tone="muted">
                CONTA
              </Text>
              {/* Só aparece quando a igreja cadastrou alguma. Um campo de conta
                  vazio numa igreja de caixa único é pergunta sem resposta. */}
              <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
                <Chip label="Não informar" selected={contaId === null} onPress={() => setContaId(null)} />
                {contas.map((conta) => (
                  <Chip
                    key={conta.id}
                    label={conta.name}
                    selected={contaId === conta.id}
                    onPress={() => setContaId(conta.id)}
                  />
                ))}
              </View>
            </View>


          )}



          <View style={{ gap: theme.space[8] }}>
            <Text variant="eyebrow" tone="muted">
              SE REPETE
            </Text>
            {/* A frequência É a recorrência: um par "recorrente" + "frequência"
                admitiria "recorrente sem frequência", que a constraint do banco
                recusa com um erro que não explica nada. */}
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}>
              <Chip label="Não" selected={recorrencia === null} onPress={() => setRecorrencia(null)} />
              <Chip
                label="Semanal"
                selected={recorrencia === 'Semanal'}
                onPress={() => setRecorrencia('Semanal')}
              />
              <Chip
                label="Mensal"
                selected={recorrencia === 'Mensal'}
                onPress={() => setRecorrencia('Mensal')}
              />
              <Chip
                label="Anual"
                selected={recorrencia === 'Anual'}
                onPress={() => setRecorrencia('Anual')}
              />
            </View>
            {recorrencia !== null && (
              <Text variant="captionBody" tone="muted">
                Serão criadas {QUANTAS_PARCELAS[recorrencia]} cobrindo os próximos 12 meses. Elas
                aparecem na lista marcadas como{' '}
                <Text variant="captionBody" tone="accent">
                  Previsto
                </Text>{' '}
                e não entram no saldo do mês até você confirmar que o dinheiro se moveu.
              </Text>
            )}


          </View>



          {tipo === 'Saida' && (
            <View
              style={{
                gap: theme.space[12],
                padding: theme.space[16],
                borderRadius: theme.radius.smallCards,
                borderWidth: 1,
                borderColor: theme.colors.hairline,
                backgroundColor: theme.colors.surfaceInner,
              }}
            >
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: theme.space[12],
                  minWidth: 0,
                }}
              >
                <View style={{ flex: 1, minWidth: 0 }}>
                  <Text variant="eyebrow" tone="muted">
                    DOCUMENTO FISCAL
                  </Text>
                  <Text variant="captionBody" tone="muted">
                    Nota, cupom ou recibo da despesa
                  </Text>
                </View>

                <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
                  <Chip
                    label="Possui"
                    selected={temDocumento}
                    onPress={() => setTemDocumento(true)}
                  />
                  <Chip
                    label="Sem documento"
                    selected={!temDocumento}
                    onPress={() => {
                      setTemDocumento(false);
                      setAnexo(null);
                    }}
                  />
                </View>
              </View>

              {temDocumento && (
                <>
                  <Text variant="captionBody" tone="muted">
                    Anexar o comprovante ajuda a prestação de contas da tesouraria e facilita a
                    conferência anual.
                  </Text>

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
                          selected={tipoDoDocumento === opcao}
                          onPress={() => {
                            setTipoDoDocumento(opcao);
                            if (erros['documento']) setErros((e) => ({ ...e, documento: '' }));
                          }}
                        />
                      ))}
                    </View>
                  </View>

                  <TextField
                    label="Número do documento"
                    placeholder="Ex.: 000123"
                    defaultValue=""
                    onValueChange={(v) => {
                      numeroDoDocumento.current = v;
                      if (erros['documento']) setErros((e) => ({ ...e, documento: '' }));
                    }}
                    {...(erros['documento'] ? { error: erros['documento'] } : {})}
                    {...(tipoDoDocumento === 'Recibo'
                      ? { hint: 'Opcional em recibo — recibo à mão costuma não ter número.' }
                      : {})}
                  />

                  <TextField
                    label="Série"
                    placeholder="Ex.: 1"
                    defaultValue=""
                    onValueChange={(v) => {
                      serieDoDocumento.current = v;
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
                    hint="Opcional. Pode digitar com pontuação — o dígito verificador é conferido."
                  />

                  <TextField
                    label="Chave de acesso"
                    placeholder="44 dígitos da NF-e"
                    defaultValue=""
                    onValueChange={(v) => {
                      chaveDeAcesso.current = v;
                    }}
                    keyboardType="number-pad"
                    hint="Opcional. Fica no rodapé do DANFE ou no QR Code da nota."
                  />

                  <View style={{ gap: theme.space[8] }}>
                    <Text variant="eyebrow" tone="muted">
                      ANEXO DO COMPROVANTE
                    </Text>
                    <SeletorDeArquivo
                      arquivo={anexo}
                      onEscolher={setAnexo}
                      onRemover={() => setAnexo(null)}
                    />
                  </View>
                </>
              )}
            </View>
          )}

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
