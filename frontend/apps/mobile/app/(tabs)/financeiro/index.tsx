import {
  confirmGivingEntry,
  deleteGivingEntry,
  type GivingEntry,
} from '@congrega/api-client/giving';
import { formatDate } from '@congrega/core/datetime';
import { cents, formatBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { MonthNavigator } from '@congrega/ui/MonthNavigator';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Alert, Platform, Pressable, View, useWindowDimensions } from 'react-native';
import { apiClient } from '../../../src/api';
import { PODE_EXPORTAR, exportarLancamentosDoMes } from '../../../src/exportarLancamentos';
import { ROTULO_DA_FREQUENCIA, ROTULO_DO_METODO } from '../../../src/rotulosFinanceiros';
import {
  deslocarMes,
  mesCorrente,
  nomeDoMes,
  useGivingCategories,
  useGivingEntries,
  useMonthlyClosing,
} from '../../../src/useGiving';

/** Abaixo disto os três cartões de resumo empilham. */
const LARGURA_PARA_TRES_COLUNAS = 900;
/** Abaixo disto a lista e o resumo por categoria deixam de dividir a linha. */
const LARGURA_PARA_DUAS_COLUNAS = 1180;

export default function Financeiro() {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const [periodo, setPeriodo] = useState(mesCorrente);
  const [busca, setBusca] = useState('');
  const [exportando, setExportando] = useState(false);
  const [avisoDeExportacao, setAvisoDeExportacao] = useState<string | null>(null);

  // `undefined` é "todos". Guardar o tipo e a categoria juntos evita o estado
  // inconsistente de ter os dois chips ativos ao mesmo tempo mostrando
  // resultados que nenhum dos dois prometeu.
  const [filtro, setFiltro] = useState<{
    readonly kind?: 'Entrada' | 'Saida';
    readonly categoryId?: string;
  }>({});

  const { lancamentos, carregando, erro, resumo, recarregar } = useGivingEntries(
    periodo.year,
    periodo.month,
    filtro,
  );

  const { categorias } = useGivingCategories();
  const { fechamento, recarregar: recarregarFechamento } = useMonthlyClosing(
    periodo.year,
    periodo.month,
  );

  const emTresColunas = width >= LARGURA_PARA_TRES_COLUNAS;
  const emDuasColunas = width >= LARGURA_PARA_DUAS_COLUNAS;

  // Voltar da tela de lançar precisa refletir o que acabou de ser lançado. Sem
  // isso, o tesoureiro digita, volta e não vê — e lança de novo.
  useFocusEffect(
    useCallback(() => {
      recarregar();
      recarregarFechamento();
    }, [recarregar, recarregarFechamento]),
  );

  /**
   * A busca filtra no CLIENTE, sobre o mês já carregado.
   *
   * O servidor tem `?search=`, e usá-lo custaria uma ida à rede por tecla. O mês
   * inteiro já está em memória — são até 100 lançamentos — e filtrar aqui
   * responde instantaneamente. Se um dia a listagem passar a paginar de verdade
   * dentro do mês, isto precisa voltar para o servidor, porque aí o filtro
   * estaria vendo só um pedaço.
   */
  const visiveis = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    if (termo === '') return lancamentos;

    return lancamentos.filter((l) =>
      [l.description, l.categoryName, l.notes, l.memberName, l.accountName]
        .filter((c): c is string => c !== null && c !== undefined)
        .some((c) => c.toLowerCase().includes(termo)),
    );
  }, [lancamentos, busca]);

  /**
   * Linhas do resumo por categoria, com a cor e o percentual.
   *
   * O percentual é **dentro do próprio tipo** — "53% das entradas", não "53% do
   * mês". Misturar entrada e saída num mesmo denominador produziria um número
   * que não significa nada: R$ 100 de dízimo não são uma fração de R$ 1.200 de
   * aluguel.
   */
  const porCategoria = useMemo(() => {
    if (fechamento === null) return [];

    const totalEntradas = fechamento.totalIncomeCents;
    const totalSaidas = fechamento.totalExpenseCents;

    return fechamento.lines.map((linha) => {
      const ehSaida = linha.kind === 'Saida';
      const denominador = ehSaida ? totalSaidas : totalEntradas;

      return {
        ...linha,
        ehSaida,
        // Divisão por zero não acontece — uma linha só existe se houve
        // movimento no seu tipo — mas o guarda evita `NaN` na largura da barra
        // se o fechamento e as linhas ficarem fora de sincronia.
        percentual: denominador === 0 ? 0 : Math.round((linha.totalCents / denominador) * 100),
        cor:
          categorias.find((c) => c.id === linha.categoryId)?.colorHex ??
          (ehSaida ? theme.colors.danger : theme.colors.surfaceAccent),
      };
    });
  }, [fechamento, categorias, theme.colors]);

  /**
   * Quantos lançamentos cada total representa.
   *
   * **Sai do fechamento, e não do resumo dos chips** — e a diferença é o ponto.
   * O resumo conta o que a LISTA mostra, que inclui o previsto; o fechamento
   * soma só o realizado. Misturar os dois produzia um cartão dizendo "4
   * lançamentos" ao lado de um valor que continha três — o tesoureiro que
   * conferisse a conta na mão encontraria uma diferença sem explicação.
   *
   * Vindo do mesmo objeto que o valor, os dois não têm como divergir.
   */
  const quantidadeRealizada = useMemo(() => {
    if (fechamento === null) return null;

    const somar = (tipo: 'Entrada' | 'Saida') =>
      fechamento.lines.filter((l) => l.kind === tipo).reduce((total, l) => total + l.entryCount, 0);

    return { entradas: somar('Entrada'), saidas: somar('Saida') };
  }, [fechamento]);

  async function apagar(lancamento: GivingEntry) {
    try {
      await deleteGivingEntry(apiClient, lancamento.id);
      recarregar();
      recarregarFechamento();
    } catch {
      // O erro reaparece no recarregamento da lista; um alerta a mais aqui
      // empilharia dois avisos para a mesma falha.
      recarregar();
    }
  }

  /**
   * Marca um previsto como realizado.
   *
   * O servidor recusa data futura com 409 e o motivo escrito — a tela não
   * oferece o botão nesse caso, mas a barreira continua no domínio, onde a data
   * é comparada com o relógio do servidor. O do aparelho pode estar errado.
   */
  async function confirmar(lancamento: GivingEntry) {
    try {
      await confirmGivingEntry(apiClient, lancamento.id);
    } finally {
      // Recarrega mesmo em falha: se outra aba já confirmou, a lista precisa
      // parar de oferecer o botão.
      recarregar();
      recarregarFechamento();
    }
  }

  function confirmarExclusao(lancamento: GivingEntry) {
    const titulo = lancamento.description ?? lancamento.categoryName;
    const descricao = `${titulo} · ${formatBRL(cents(lancamento.amountCents))}`;

    // `Alert` não existe no web do react-native-web com botões; `confirm` é o
    // equivalente nativo do navegador e evita uma dependência de modal só para
    // esta confirmação.
    if (Platform.OS === 'web') {
      // eslint-disable-next-line no-alert
      if (globalThis.confirm(`Apagar o lançamento ${descricao}?`)) {
        void apagar(lancamento);
      }
      return;
    }

    Alert.alert('Apagar lançamento', `${descricao} será removido do caixa.`, [
      { text: 'Cancelar', style: 'cancel' },
      { text: 'Apagar', style: 'destructive', onPress: () => void apagar(lancamento) },
    ]);
  }

  async function exportar() {
    setAvisoDeExportacao(null);
    setExportando(true);

    try {
      const { quantidade, nomeDoArquivo } = await exportarLancamentosDoMes(
        periodo.year,
        periodo.month,
      );

      setAvisoDeExportacao(
        quantidade === 0
          ? 'Nada para exportar neste mês.'
          : `${quantidade} ${quantidade === 1 ? 'lançamento exportado' : 'lançamentos exportados'} em ${nomeDoArquivo}.`,
      );
    } catch {
      setAvisoDeExportacao('Não deu para gerar o arquivo. Tente de novo.');
    } finally {
      setExportando(false);
    }
  }

  const saldo = fechamento?.balanceCents ?? 0;

  return (
    <Screen padded={false} wide>
      <View
        style={{
          width: '100%',
          maxWidth: theme.layout.pageMaxWidth,
          alignSelf: 'center',
          paddingHorizontal: theme.space[20],
          paddingTop: theme.space[32],
          paddingBottom: theme.space[28],
          gap: theme.space[16],
        }}
      >
        {/* ---------------------------------------------------------- herói */}
        <View
          style={{
            flexDirection: emTresColunas ? 'row' : 'column',
            alignItems: emTresColunas ? 'flex-end' : 'stretch',
            justifyContent: 'space-between',
            gap: theme.space[16],
          }}
        >
          <View>
            <Text variant="eyebrow" tone="muted">
              SUA IGREJA
            </Text>
            <Text variant="headingLg" style={{ marginTop: theme.space[8] }}>
              Financeiro
            </Text>
          </View>

          <MonthNavigator
            label={`${nomeDoMes(periodo.month)} de ${periodo.year}`}
            year={periodo.year}
            month={periodo.month}
            onChange={(passos) => setPeriodo((atual) => deslocarMes(atual, passos))}
            onSelect={(year, month) => setPeriodo({ year, month })}
          />
        </View>

        {/* ------------------------------------------- cartões de resumo */}
        <View style={{ flexDirection: emTresColunas ? 'row' : 'column', gap: theme.space[16] }}>
          <CartaoDeTotal
            categoria="ENTRADAS"
            icone="arrow-up"
            tom="accent"
            valorCents={fechamento?.totalIncomeCents ?? 0}
            previstoCents={fechamento?.plannedIncomeCents ?? 0}
            {...(quantidadeRealizada === null
              ? {}
              : {
                  nota: `${quantidadeRealizada.entradas} ${quantidadeRealizada.entradas === 1 ? 'lançamento' : 'lançamentos'} este mês`,
                })}
          />

          <CartaoDeTotal
            categoria="SAÍDAS"
            icone="arrow-down"
            tom="perigo"
            valorCents={fechamento?.totalExpenseCents ?? 0}
            previstoCents={fechamento?.plannedExpenseCents ?? 0}
            {...(quantidadeRealizada === null
              ? {}
              : {
                  nota: `${quantidadeRealizada.saidas} ${quantidadeRealizada.saidas === 1 ? 'lançamento' : 'lançamentos'} este mês`,
                })}
          />

          <CartaoDeTotal
            categoria="SALDO DO MÊS"
            icone="pie-chart"
            // Saldo negativo é a informação mais importante da tela: é o mês em
            // que a igreja gastou mais do que arrecadou.
            tom={saldo < 0 ? 'perigo' : 'accent'}
            valorCents={saldo}
            comSinal
            nota={
              saldo < 0 ? 'Saídas maiores que entradas'
              : saldo > 0 ? 'Entradas maiores que saídas'
              : 'Entradas e saídas empatadas'
            }
          />
        </View>

        {/* -------------------------------------------------- barra de ações */}
        <View
          style={{
            flexDirection: emTresColunas ? 'row' : 'column',
            alignItems: emTresColunas ? 'center' : 'stretch',
            gap: theme.space[12],
          }}
        >
          <View style={{ flex: emTresColunas ? 1 : undefined }}>
            <SignatureButton label="Lançar" onPress={() => router.push('/financeiro/lancar')} />
          </View>

          <Button label="Categorias" variant="outline" onPress={() => router.push('/financeiro/categorias')} />

          {/* O botão só existe onde a exportação funciona.
              Baixar arquivo em iOS/Android exige sistema de arquivos mais folha
              de compartilhamento; um botão que aparece no celular e não faz nada
              ensina o usuário a desconfiar dos outros. */}
          {PODE_EXPORTAR && (
            <Button
              label={exportando ? 'Exportando…' : 'Exportar'}
              variant="outline"
              disabled={exportando}
              onPress={() => void exportar()}
            />
          )}

          <View style={{ flex: emTresColunas ? 1 : undefined, minWidth: 180 }}>
            <TextField
              label="Buscar"
              placeholder="Buscar lançamento"
              defaultValue=""
              onValueChange={setBusca}
              autoCapitalize="none"
              autoCorrect={false}
            />
          </View>
        </View>

        {/* Quando há previsto no mês, os chips e os cartões contam coisas
            diferentes de propósito — e a tela precisa dizer isso, senão a
            diferença entre "Saídas 4" e "3 lançamentos" parece erro. */}
        {lancamentos.some((l) => l.status === 'Previsto') && (
          <Text variant="captionBody" tone="muted">
            Os totais acima somam apenas o que já foi realizado. Os filtros abaixo contam também os
            lançamentos previstos.
          </Text>
        )}

        {avisoDeExportacao !== null && (
          <Text variant="captionBody" tone="muted" accessibilityLiveRegion="polite">
            {avisoDeExportacao}
          </Text>
        )}

        {/* ------------------------------------------------- chips de filtro */}
        {resumo !== null && (
          <View
            style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
            accessibilityRole="radiogroup"
          >
            <ChipDeFiltro
              rotulo="Todos"
              quantidade={resumo.total}
              ativo={filtro.kind === undefined && filtro.categoryId === undefined}
              onPress={() => setFiltro({})}
            />
            <ChipDeFiltro
              rotulo="Entradas"
              quantidade={resumo.entradas}
              ativo={filtro.kind === 'Entrada'}
              onPress={() => setFiltro({ kind: 'Entrada' })}
            />
            <ChipDeFiltro
              rotulo="Saídas"
              quantidade={resumo.saidas}
              ativo={filtro.kind === 'Saida'}
              onPress={() => setFiltro({ kind: 'Saida' })}
            />

            {/* Só as categorias COM lançamento no mês. Uma categoria zerada
                ocuparia um chip para dizer que não há nada nela. */}
            {categorias
              .filter((c) => (resumo.porCategoria[c.id] ?? 0) > 0)
              .map((categoria) => (
                <ChipDeFiltro
                  key={categoria.id}
                  rotulo={categoria.name}
                  quantidade={resumo.porCategoria[categoria.id] ?? 0}
                  cor={categoria.colorHex}
                  ativo={filtro.categoryId === categoria.id}
                  onPress={() => setFiltro({ categoryId: categoria.id })}
                />
              ))}
          </View>
        )}

        {/* -------------------------------------------- lista + por categoria */}
        <View
          style={{
            flexDirection: emDuasColunas ? 'row' : 'column',
            gap: theme.space[16],
            alignItems: 'flex-start',
          }}
        >
          <Card
            style={{
              flex: emDuasColunas ? 1.75 : undefined,
              width: emDuasColunas ? undefined : '100%',
              gap: theme.space[16],
            }}
          >
            <View>
              <Text variant="eyebrow" tone="muted">
                {`${nomeDoMes(periodo.month)} de ${periodo.year}`.toUpperCase()}
              </Text>
              <Text variant="headingSm" style={{ marginTop: theme.space[4] }}>
                Lançamentos{' '}
                <Text variant="captionBody" tone="muted">
                  · {visiveis.length}
                </Text>
              </Text>
            </View>

            <AsyncContent
              loading={carregando}
              skeleton={
                <View style={{ gap: theme.space[8] }}>
                  <SkeletonListRow />
                  <SkeletonListRow />
                  <SkeletonListRow />
                </View>
              }
              failure={erro}
              errorTitle="Não deu para carregar o caixa"
              onRetry={recarregar}
              isEmpty={visiveis.length === 0}
              empty={
                <EmptyState
                  title={
                    busca !== '' ? 'Nenhum lançamento encontrado'
                    : `Nenhum lançamento em ${nomeDoMes(periodo.month)}`
                  }
                  description={
                    busca !== ''
                      ? 'Tente outro termo, ou limpe a busca para ver o mês inteiro.'
                      : 'Registre as entradas e saídas do mês para fechar as contas sem planilha.'
                  }
                  {...(busca === ''
                    ? {
                        action: (
                          <SignatureButton
                            label="Lançar"
                            onPress={() => router.push('/financeiro/lancar')}
                          />
                        ),
                      }
                    : {})}
                />
              }
            >
              <View style={{ gap: theme.space[8] }}>
                {visiveis.map((lancamento) => (
                  <LinhaDeLancamento
                    key={lancamento.id}
                    lancamento={lancamento}
                    onAbrir={() =>
                      router.push({
                        pathname: '/financeiro/[id]',
                        params: { id: lancamento.id },
                      })
                    }
                    onConfirmar={() => void confirmar(lancamento)}
                    onApagar={() => confirmarExclusao(lancamento)}
                  />
                ))}
              </View>

              <View
                style={{
                  marginTop: theme.space[16],
                  paddingTop: theme.space[16],
                  borderTopWidth: 1,
                  borderTopColor: theme.colors.hairline,
                }}
              >
                <Text variant="captionBody" tone="muted">
                  Mostrando {visiveis.length} de {lancamentos.length}{' '}
                  {lancamentos.length === 1 ? 'lançamento' : 'lançamentos'}
                </Text>
              </View>
            </AsyncContent>
          </Card>

          <Card
            style={{
              flex: emDuasColunas ? 0.95 : undefined,
              width: emDuasColunas ? undefined : '100%',
              gap: theme.space[16],
            }}
          >
            <View>
              <Text variant="eyebrow" tone="muted">
                POR CATEGORIA
              </Text>
              <Text variant="headingSm" style={{ marginTop: theme.space[4] }}>
                Resumo do mês
              </Text>
            </View>

            {porCategoria.length === 0 ? (
              <Text variant="captionBody" tone="muted">
                Nenhum lançamento realizado neste mês.
              </Text>
            ) : (
              <View style={{ gap: theme.space[16] }}>
                {porCategoria.map((linha) => (
                  <BarraDeCategoria key={`${linha.categoryId}-${linha.kind}`} {...linha} />
                ))}
              </View>
            )}

            <View
              style={{
                paddingTop: theme.space[16],
                borderTopWidth: 1,
                borderTopColor: theme.colors.hairline,
                gap: theme.space[8],
              }}
            >
              <AtalhoFinanceiro
                icone="arrow-up"
                rotulo="Lançar entrada"
                onPress={() => router.push('/financeiro/lancar')}
              />
              <AtalhoFinanceiro
                icone="arrow-down"
                rotulo="Lançar saída"
                onPress={() => router.push('/financeiro/lancar')}
              />
              <AtalhoFinanceiro
                icone="list"
                rotulo="Gerenciar categorias"
                onPress={() => router.push('/financeiro/categorias')}
              />
              <AtalhoFinanceiro
                icone="lock"
                rotulo="Cofre da igreja"
                onPress={() => router.push('/financeiro/cofre')}
              />
            </View>
          </Card>
        </View>

        <Text variant="captionBody" tone="muted">
          Congrega · Gestão simples para uma comunidade viva
        </Text>
      </View>
    </Screen>
  );
}

function CartaoDeTotal({
  categoria,
  icone,
  tom,
  valorCents,
  nota,
  comSinal = false,
  previstoCents = 0,
}: {
  readonly categoria: string;
  readonly icone: keyof typeof Feather.glyphMap;
  readonly tom: 'accent' | 'perigo';
  readonly valorCents: number;
  readonly nota?: string;
  readonly comSinal?: boolean;

  /**
   * Quanto ainda está PREVISTO neste mês.
   *
   * Desenhado abaixo do total, e nunca somado a ele. Foi a ausência desta linha
   * que fez o caixa parecer quebrado: setembro mostrava R$ 0,00 ao lado de uma
   * lista com um aluguel de R$ 1.200, e não havia como saber que o valor
   * continuava lá, apenas ainda não pago.
   */
  readonly previstoCents?: number;
}) {
  const theme = useTheme();

  const fundo = tom === 'accent' ? theme.colors.surfaceAccentSoft : '#FBE9E7';
  const cor = tom === 'accent' ? theme.colors.textOnAccentSoft : theme.colors.danger;

  return (
    <Card style={{ flex: 1, flexDirection: 'row', gap: theme.space[16], minHeight: 128 }}>
      {/* O ícone é reforço: o rótulo escrito acima do número é quem diz o que
          ele conta. Ver a nota sobre luminância em `tokens.test.ts`. */}
      <View
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        style={{
          width: 46,
          height: 46,
          borderRadius: theme.radius.smallCards,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: fundo,
        }}
      >
        <Feather name={icone} size={22} color={cor} />
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="eyebrow" tone="muted">
          {categoria}
        </Text>

        <Text
          variant="heading"
          style={{
            marginTop: theme.space[4],
            // O saldo negativo é o único número que muda de cor: ele é a
            // informação que a tela existe para não deixar passar.
            ...(comSinal && valorCents < 0 ? { color: theme.colors.danger } : {}),
          }}
        >
          {comSinal && valorCents < 0 ? '−' : ''}
          {formatBRL(cents(Math.abs(valorCents)))}
        </Text>

        {previstoCents > 0 && (
          <Text
            variant="captionBody"
            tone="muted"
            style={{ marginTop: theme.space[8] }}
          >
            {/* "ainda previsto", e não "+ R$ 1.200": o sinal de mais convidaria
                a somar de cabeça com o número de cima, que é exatamente o que
                este campo existe para impedir. */}
            {formatBRL(cents(previstoCents))} ainda previsto
          </Text>
        )}

        {nota !== undefined && (
          <Text variant="captionBody" tone="muted" style={{ marginTop: theme.space[4] }}>
            {nota}
          </Text>
        )}
      </View>
    </Card>
  );
}

function BarraDeCategoria({
  categoryName,
  totalCents,
  percentual,
  ehSaida,
  cor,
}: {
  readonly categoryName: string;
  readonly totalCents: number;
  readonly percentual: number;
  readonly ehSaida: boolean;
  readonly cor: string;
}) {
  const theme = useTheme();

  return (
    <View
      style={{ gap: theme.space[8] }}
      accessible
      // Uma frase só: separados, o nome, o valor e o percentual seriam três
      // fragmentos que o leitor de tela anuncia sem relação entre si.
      accessibilityLabel={`${categoryName}, ${formatBRL(cents(totalCents))}, ${percentual}% ${ehSaida ? 'das saídas' : 'das entradas'}`}
    >
      <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: theme.space[8] }}>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexShrink: 1, minWidth: 0 }}>
          <View style={{ width: 9, height: 9, borderRadius: 5, backgroundColor: cor, flexShrink: 0 }} />
          <Text variant="caption" numberOfLines={1} style={{ flexShrink: 1, minWidth: 0 }}>
            {categoryName}
          </Text>
        </View>

        <Text variant="caption">{formatBRL(cents(totalCents))}</Text>
      </View>

      <View
        style={{
          height: 7,
          borderRadius: theme.radius.tags,
          overflow: 'hidden',
          backgroundColor: theme.colors.surfaceInner,
          borderWidth: 1,
          borderColor: theme.colors.hairline,
        }}
      >
        <View style={{ height: '100%', width: `${percentual}%`, backgroundColor: cor }} />
      </View>

      <Text variant="captionBody" tone="muted">
        {percentual}% {ehSaida ? 'das saídas' : 'das entradas'}
      </Text>
    </View>
  );
}

function AtalhoFinanceiro({
  icone,
  rotulo,
  onPress,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly rotulo: string;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={rotulo}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        minHeight: theme.touch.minTarget,
        paddingHorizontal: theme.space[12],
        borderRadius: theme.radius.inputs,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: pressed ? theme.colors.surfaceAccentSoft : theme.colors.surfaceInner,
      })}
    >
      <Feather name={icone} size={16} color={theme.colors.surfaceAccent} />
      <Text variant="caption">{rotulo}</Text>
    </Pressable>
  );
}

function LinhaDeLancamento({
  lancamento,
  onAbrir,
  onConfirmar,
  onApagar,
}: {
  readonly lancamento: GivingEntry;
  readonly onAbrir: () => void;
  readonly onConfirmar: () => void;
  readonly onApagar: () => void;
}) {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const isSaida = lancamento.kind === 'Saida';
  const previsto = lancamento.status === 'Previsto';
  const emLinha = width >= 620;

  /**
   * A data já chegou?
   *
   * Comparação de TEXTO em formato ISO, que ordena igual à cronologia e não
   * passa por `Date` — construir uma data a partir de `2026-09-30` a
   * interpretaria como meia-noite UTC e, em São Paulo, diria que 30/09 ainda não
   * chegou às 21h do dia 30. É o mesmo deslize de fuso já corrigido em
   * `formatDate`.
   */
  const hojeIso = new Date().toLocaleDateString('sv-SE');
  const podeConfirmar = previsto && lancamento.occurredOn <= hojeIso;

  /**
   * Título, com queda para o nome da categoria.
   *
   * Os lançamentos gravados antes de o título existir não têm um, e preenchê-lo
   * com o nome da categoria no servidor produziria a repetição ("Dízimo /
   * Categoria: Dízimo") que o campo veio resolver. A queda acontece só aqui.
   */
  const titulo = lancamento.description ?? lancamento.categoryName;

  // A segunda linha omite "Categoria: X" quando o título JÁ é o nome dela.
  const subtitulo = [
    lancamento.description === null ? null : `Categoria: ${lancamento.categoryName}`,
    lancamento.isRecurring && lancamento.recurrence !== null
      ? `Recorrente (${ROTULO_DA_FREQUENCIA[lancamento.recurrence]})`
      : null,
  ].filter((p): p is string => p !== null);

  const meta = [
    formatDate(`${lancamento.occurredOn}T12:00:00Z`),
    ROTULO_DO_METODO[lancamento.method] ?? lancamento.method,
    lancamento.accountName,
    lancamento.memberName,
  ].filter((m): m is string => m !== null && m !== '');

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: emLinha ? 'center' : 'flex-start',
        gap: theme.space[12],
        padding: theme.space[12],
        borderRadius: theme.radius.smallCards,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: theme.colors.surface,
        // O previsto é atenuado e tracejado — mas nunca SÓ isso: o selo
        // "Previsto" ao lado do título é o que carrega o estado para quem não
        // percebe a diferença de opacidade.
        ...(previsto ? { opacity: 0.72, borderStyle: 'dashed' as const } : {}),
      }}
    >
      <Pressable
        onPress={onAbrir}
        accessibilityRole="link"
        accessibilityLabel={`Ver detalhes de ${titulo}`}
        style={{
          flex: 1,
          minWidth: 0,
          flexDirection: 'row',
          alignItems: emLinha ? 'center' : 'flex-start',
          gap: theme.space[12],
        }}
      >
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{
            width: 44,
            height: 44,
            borderRadius: theme.radius.smallCards,
            flexShrink: 0,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: isSaida ? '#FBEAEA' : theme.colors.surfaceAccentSoft,
          }}
        >
          <Feather
            name={isSaida ? 'arrow-down' : 'arrow-up'}
            size={19}
            color={isSaida ? theme.colors.danger : theme.colors.textOnAccentSoft}
          />
        </View>

        <View
          style={{
            flex: 1,
            minWidth: 0,
            flexDirection: emLinha ? 'row' : 'column',
            alignItems: emLinha ? 'center' : 'flex-start',
            gap: emLinha ? theme.space[12] : theme.space[4],
          }}
        >
        <View style={{ flex: emLinha ? 1.6 : undefined, minWidth: 0, width: emLinha ? undefined : '100%' }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexWrap: 'wrap' }}>
            <Text variant="bodyStrong" numberOfLines={1} style={{ flexShrink: 1, minWidth: 0 }}>
              {titulo}
            </Text>

            {/* O selo aparece em TODA linha, e não só nas saídas: antes a
                ausência de selo tinha de ser lida como "entrada", o que exige
                saber que a regra existe. */}
            <EyebrowPill label={isSaida ? 'Saída' : 'Entrada'} tone="badge" />

            {previsto && <EyebrowPill label="Previsto" tone="badge" />}
          </View>

          {subtitulo.length > 0 && (
            <Text variant="captionBody" tone="muted" numberOfLines={1} style={{ marginTop: 3 }}>
              {subtitulo.join(' · ')}
            </Text>
          )}
        </View>

        <View
          style={{
            flex: emLinha ? 1 : undefined,
            width: emLinha ? undefined : '100%',
            minWidth: 0,
            flexDirection: 'row',
            flexWrap: 'wrap',
            gap: theme.space[12],
          }}
        >
          <Text variant="captionBody" tone="muted" numberOfLines={2}>
            {meta.join(' · ')}
          </Text>
        </View>

        <Text
          variant="bodyStrong"
          style={{
            color: isSaida ? theme.colors.danger : theme.colors.textOnAccentSoft,
            textAlign: 'right',
          }}
        >
          {isSaida ? '−' : '+'} {formatBRL(cents(lancamento.amountCents))}
        </Text>
        </View>
      </Pressable>

      {/* **Só aparece quando a confirmação pode dar certo.**
          O domínio recusa confirmar data futura — a parcela de dezembro não
          pode ser marcada como paga em agosto. Oferecer o botão assim mesmo
          produziria um erro para uma ação que a tela sugeriu. */}
      {podeConfirmar && (
        <Pressable
          onPress={onConfirmar}
          accessibilityRole="button"
          accessibilityLabel={`Confirmar que ${titulo} foi pago`}
          hitSlop={8}
          style={({ pressed }) => ({
            height: 34,
            flexShrink: 0,
            paddingHorizontal: theme.space[12],
            borderRadius: theme.radius.tags,
            borderWidth: 1,
            borderColor: theme.colors.surfaceAccent,
            alignItems: 'center',
            justifyContent: 'center',
            backgroundColor: pressed
              ? theme.colors.surfaceAccentSoft
              : theme.colors.surface,
          })}
        >
          <Text variant="caption" style={{ color: theme.colors.textOnAccentSoft }}>
            Confirmar
          </Text>
        </Pressable>
      )}

      <Pressable
        onPress={onApagar}
        accessibilityRole="button"
        accessibilityLabel={`Apagar lançamento ${titulo}`}
        hitSlop={8}
        style={({ pressed }) => ({
          width: 34,
          height: 34,
          flexShrink: 0,
          borderRadius: theme.radius.inputs,
          borderWidth: 1,
          borderColor: theme.colors.hairline,
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: pressed ? '#FBEAEA' : theme.colors.surfaceInner,
        })}
      >
        <Feather name="trash-2" size={15} color={theme.colors.danger} />
      </Pressable>
    </View>
  );
}

function ChipDeFiltro({
  rotulo,
  quantidade,
  ativo,
  cor,
  onPress,
}: {
  readonly rotulo: string;
  readonly quantidade: number;
  readonly ativo: boolean;
  readonly cor?: string | null;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ checked: ativo }}
      // Rótulo e contagem como UMA frase: separados, o leitor de tela os
      // anuncia como fragmentos sem relação.
      accessibilityLabel={`${rotulo}, ${quantidade}`}
      style={({ pressed }) => ({
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[8],
        minHeight: theme.touch.minTarget,
        paddingHorizontal: theme.space[12],
        borderRadius: theme.radius.tags,
        borderWidth: 1,
        borderColor: ativo ? theme.colors.surfaceAccent : theme.colors.hairline,
        backgroundColor:
          ativo ? theme.colors.surfaceAccentSoft
          : pressed ? theme.colors.surfaceInner
          : theme.colors.surface,
      })}
    >
      {/* Ponto da categoria. Decorativo: o nome está escrito ao lado, e cor
          sozinha some para quem não distingue matiz. */}
      {cor !== undefined && cor !== null && (
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: cor }}
        />
      )}

      <Text variant="caption" tone={ativo ? 'accent' : 'muted'}>
        {rotulo}
      </Text>
      <Text variant="caption" tone={ativo ? 'accent' : 'ink'}>
        {quantidade}
      </Text>
    </Pressable>
  );
}
