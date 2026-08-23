import {
  deleteEvent,
  deleteEventSeries,
  type CalendarEvent,
} from '@congrega/api-client/events';
import { formatTime, monthName, shiftMonth, type YearMonth } from '@congrega/core/datetime';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { MonthNavigator } from '@congrega/ui/MonthNavigator';
import { Screen } from '@congrega/ui/Screen';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { TextField } from '@congrega/ui/TextField';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Alert, Platform, Pressable, ScrollView, View, useWindowDimensions } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { useEventsOfMonth } from '../../../src/useEvents';

/** Abaixo disto o cabeçalho e a barra de mês empilham. */
const LARGURA_PARA_LINHA = 900;

/** Abaixo disto os cartões de resumo passam de quatro colunas para duas. */
const LARGURA_PARA_QUATRO = 1180;

/** Largura da coluna de data. Fixa: é ela que alinha os títulos numa régua só. */
const LARGURA_DO_BLOCO_DE_DATA = 52;

function mesCorrente(): YearMonth {
  const agora = new Date();
  return { year: agora.getFullYear(), month: agora.getMonth() + 1 };
}

function ehMesCorrente(periodo: YearMonth): boolean {
  const atual = mesCorrente();
  return periodo.year === atual.year && periodo.month === atual.month;
}

/**
 * Bloco de data da linha — `Dom` / `16` / `Ago`.
 *
 * Três formatadores em vez de um porque as três partes têm pesos tipográficos
 * diferentes; um `format()` só devolveria a string montada e obrigaria a
 * fatiá-la de volta.
 *
 * O fuso é fixo em `America/Sao_Paulo`, e não no do dispositivo: a data que
 * importa é a do evento na igreja. Um membro viajando não deve ver o culto de
 * domingo cair no sábado.
 */
const FORMATO_SEMANA = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo',
  weekday: 'short',
});
const FORMATO_DIA = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo',
  day: '2-digit',
});
const FORMATO_MES = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo',
  month: 'short',
});

/** `2026-08-22` no fuso da igreja — chave de agrupamento por dia. */
const CHAVE_DO_DIA = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'America/Sao_Paulo',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

function capitalizar(texto: string): string {
  const limpo = texto.replace('.', '');
  return limpo.charAt(0).toUpperCase() + limpo.slice(1);
}

function iconeDe(nome: string): keyof typeof Feather.glyphMap {
  return nome in Feather.glyphMap ? (nome as keyof typeof Feather.glyphMap) : 'calendar';
}

/** Uma linha da lista: o evento e se ele abre um novo dia. */
interface Linha {
  readonly evento: CalendarEvent;
  readonly abreDia: boolean;
}

export default function Agenda() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();

  const [periodo, setPeriodo] = useState<YearMonth>(mesCorrente);
  const [busca, setBusca] = useState('');
  const [filtroDeTipo, setFiltroDeTipo] = useState<string | null>(null);

  const { eventos, carregando, erro, recarregar } = useEventsOfMonth(periodo);

  const emLinha = width >= LARGURA_PARA_LINHA;
  const emQuatro = width >= LARGURA_PARA_QUATRO;

  // Voltar de agendar ou editar precisa refletir o que mudou.
  useFocusEffect(
    useCallback(() => {
      recarregar();
    }, [recarregar]),
  );

  /**
   * Contagens por tipo, sobre o mês inteiro.
   *
   * Agrupa pelos tipos que os eventos deste mês realmente têm, em vez de
   * percorrer o catálogo da igreja: uma igreja com quinze tipos cadastrados e
   * três em uso no mês renderizaria doze chips de zero.
   */
  const resumo = useMemo(() => {
    const contagens = new Map<
      string,
      { readonly nome: string; readonly icone: string; readonly cor: string | null; quantidade: number }
    >();

    let semTipo = 0;

    for (const evento of eventos) {
      if (evento.type === null) {
        semTipo += 1;
        continue;
      }

      const atual = contagens.get(evento.type.id);

      if (atual === undefined) {
        contagens.set(evento.type.id, {
          nome: evento.type.name,
          icone: evento.type.icon,
          cor: evento.type.colorHex,
          quantidade: 1,
        });
      } else {
        atual.quantidade += 1;
      }
    }

    // Ordem alfabética, e não por contagem: ordenar por quantidade faria os
    // cartões trocarem de lugar a cada evento agendado, e quem aprendeu "vigília
    // é o segundo" perderia a referência todo mês.
    const porTipo = [...contagens.entries()]
      .map(([id, dados]) => ({ id, ...dados }))
      .sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'));

    return {
      total: eventos.length,
      porTipo,
      semTipo,
      cancelados: eventos.filter((e) => e.status === 'Cancelado').length,
    };
  }, [eventos]);

  /**
   * A busca e o filtro trabalham no CLIENTE, sobre o mês já carregado.
   *
   * O mês inteiro já está em memória — algumas dezenas de linhas no pior caso —
   * e filtrar aqui responde instantaneamente. Se um dia a agenda paginar dentro
   * do mês, isto precisa voltar para o servidor: aí o filtro estaria vendo só um
   * pedaço e diria "nenhum evento" sobre uma agenda cheia.
   */
  const visiveis = useMemo(() => {
    const termo = busca.trim().toLowerCase();

    return eventos.filter((evento) => {
      if (filtroDeTipo !== null) {
        const id = evento.type?.id ?? 'sem-tipo';
        if (id !== filtroDeTipo) return false;
      }

      if (termo === '') return true;

      return [evento.title, evento.location, evento.type?.name]
        .filter((campo): campo is string => campo !== null && campo !== undefined)
        .some((campo) => campo.toLowerCase().includes(termo));
    });
  }, [eventos, busca, filtroDeTipo]);

  /** Marca quais linhas abrem um dia novo, para a coluna de data não repetir. */
  const linhas: readonly Linha[] = useMemo(() => {
    let diaAnterior: string | null = null;

    return visiveis.map((evento) => {
      const dia = CHAVE_DO_DIA.format(new Date(evento.startsAt));
      const abreDia = dia !== diaAnterior;
      diaAnterior = dia;

      return { evento, abreDia };
    });
  }, [visiveis]);

  async function apagar(evento: CalendarEvent) {
    try {
      await deleteEvent(apiClient, evento.id);
    } finally {
      // Recarrega mesmo em falha: se outra aba já apagou, a lista precisa
      // parar de mostrar a linha.
      recarregar();
    }
  }

  async function apagarSerie(evento: CalendarEvent) {
    if (evento.seriesId === null) return;

    try {
      await deleteEventSeries(apiClient, evento.seriesId);
    } finally {
      recarregar();
    }
  }

  /**
   * Apagar um evento de série pergunta o QUE apagar.
   *
   * Um "apagar" que remove só a ocorrência deixa cinquenta e uma para trás; um
   * que remove tudo apaga o ano inteiro sem avisar. A pergunta é curta e evita
   * os dois erros — e o padrão do diálogo é a ocorrência, que é o dano menor
   * quando alguém confirma sem ler.
   */
  function confirmarExclusaoDeSerie(evento: CalendarEvent) {
    if (Platform.OS === 'web') {
      // eslint-disable-next-line no-alert
      const tudo = globalThis.confirm(
        `"${evento.title}" se repete toda semana.\n\n` +
          'OK apaga a SÉRIE INTEIRA (todos os encontros, inclusive os passados).\n' +
          'Cancelar apaga somente este encontro.',
      );

      if (tudo) {
        void apagarSerie(evento);
      } else {
        void apagar(evento);
      }
      return;
    }

    Alert.alert(
      'Apagar evento repetido',
      `"${evento.title}" se repete toda semana. O que você quer apagar?`,
      [
        { text: 'Cancelar', style: 'cancel' },
        { text: 'Só este encontro', onPress: () => void apagar(evento) },
        {
          text: 'A série inteira',
          style: 'destructive',
          onPress: () => void apagarSerie(evento),
        },
      ],
    );
  }

  function confirmarExclusao(evento: CalendarEvent) {
    // Evento de série tem uma pergunta a mais antes de qualquer exclusão.
    if (evento.seriesId !== null) {
      confirmarExclusaoDeSerie(evento);
      return;
    }

    if (Platform.OS === 'web') {
      // eslint-disable-next-line no-alert
      if (globalThis.confirm(`Apagar "${evento.title}" da agenda?`)) {
        void apagar(evento);
      }
      return;
    }

    Alert.alert('Apagar evento', `"${evento.title}" será removido da agenda.`, [
      { text: 'Cancelar', style: 'cancel' },
      { text: 'Apagar', style: 'destructive', onPress: () => void apagar(evento) },
    ]);
  }

  return (
    <Screen padded={false} wide>
      <ScrollView
        contentContainerStyle={{
          paddingHorizontal: theme.space[24],
          paddingTop: theme.space[24],
          paddingBottom: insets.bottom + theme.space[32],
          gap: theme.space[16],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        {/* ------------------------------------------------------- herói */}
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            SUA IGREJA
          </Text>
          <Text variant="headingLg">Agenda</Text>

          {/* A frase do mockup, montada a partir do que a tela realmente tem.
              Ela responde de relance a pergunta que traz alguém aqui: "tem
              alguma coisa marcada esse mês?" */}
          <Text variant="captionBody" tone="muted">
            {resumo.total === 0
              ? `Nada programado para ${monthName(periodo.month)} de ${periodo.year}.`
              : `${resumo.total} ${resumo.total === 1 ? 'evento programado' : 'eventos programados'} para ${monthName(periodo.month)} de ${periodo.year}.`}
          </Text>
        </View>

        {/* ------------------------------------------- barra do período */}
        <View
          style={{
            flexDirection: emLinha ? 'row' : 'column',
            alignItems: emLinha ? 'center' : 'stretch',
            gap: theme.space[12],
          }}
        >
          <MonthNavigator
            block
            label={`${monthName(periodo.month)} de ${periodo.year}`}
            year={periodo.year}
            month={periodo.month}
            onChange={(passos) => setPeriodo((a) => shiftMonth(a, passos))}
            onSelect={(year, month) => setPeriodo({ year, month })}
            onToday={() => setPeriodo(mesCorrente())}
            isCurrentMonth={ehMesCorrente(periodo)}
            style={{ flex: emLinha ? 1 : undefined }}
          />

          {/* O vocabulário da agenda mora numa tela própria, e não dentro do
              formulário de evento: cadastrar um tipo é administrar a igreja, não
              agendar algo. */}
          <Button label="Tipos" variant="ghost" onPress={() => router.push('/agenda/tipos')} />

          <SignatureButton
            label="Agendar evento"
            onPress={() => router.push('/agenda/novo')}
          />
        </View>

        {/* -------------------------------------------- cartões de resumo */}
        {resumo.total > 0 && (
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[12] }}>
            <CartaoDeResumo
              icone="calendar"
              cor={theme.colors.surfaceAccent}
              valor={resumo.total}
              rotulo={resumo.total === 1 ? 'Evento neste mês' : 'Eventos neste mês'}
              largura={emQuatro ? '23.5%' : emLinha ? '48.5%' : '100%'}
            />

            {resumo.porTipo.map((tipo) => (
              <CartaoDeResumo
                key={tipo.id}
                icone={iconeDe(tipo.icone)}
                cor={tipo.cor ?? theme.colors.surfaceAccent}
                valor={tipo.quantidade}
                rotulo={tipo.nome}
                largura={emQuatro ? '23.5%' : emLinha ? '48.5%' : '100%'}
              />
            ))}

            {/* "Sem tipo" só aparece quando existe. Ele diz algo acionável —
                há eventos por classificar — e some assim que não há mais. */}
            {resumo.semTipo > 0 && (
              <CartaoDeResumo
                icone="help-circle"
                cor={theme.colors.textMuted}
                valor={resumo.semTipo}
                rotulo="Sem tipo"
                largura={emQuatro ? '23.5%' : emLinha ? '48.5%' : '100%'}
              />
            )}
          </View>
        )}

        {/* ------------------------------------------------------ painel */}
        <Card style={{ gap: theme.space[16] }}>
          <View style={{ gap: theme.space[4] }}>
            <Text variant="eyebrow" tone="muted">
              {`${monthName(periodo.month).toUpperCase()} DE ${periodo.year}`}
            </Text>
            <Text variant="subheading">
              Eventos
              <Text variant="captionBody" tone="muted">
                {` · ${resumo.total}`}
              </Text>
            </Text>
          </View>

          <TextField
            label="Buscar"
            placeholder="Buscar evento por nome, local ou tipo"
            defaultValue=""
            onValueChange={setBusca}
            autoCapitalize="none"
          />

          {/* --------------------------------------------- filtros */}
          {resumo.total > 0 && (
            <View
              style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.space[8] }}
              accessibilityRole="radiogroup"
            >
              <ChipDeTipo
                rotulo="Todos"
                quantidade={resumo.total}
                ativo={filtroDeTipo === null}
                onPress={() => setFiltroDeTipo(null)}
              />

              {resumo.porTipo.map((tipo) => (
                <ChipDeTipo
                  key={tipo.id}
                  rotulo={tipo.nome}
                  quantidade={tipo.quantidade}
                  cor={tipo.cor}
                  ativo={filtroDeTipo === tipo.id}
                  onPress={() => setFiltroDeTipo(tipo.id)}
                />
              ))}

              {resumo.semTipo > 0 && (
                <ChipDeTipo
                  rotulo="Sem tipo"
                  quantidade={resumo.semTipo}
                  ativo={filtroDeTipo === 'sem-tipo'}
                  onPress={() => setFiltroDeTipo('sem-tipo')}
                />
              )}
            </View>
          )}

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
            errorTitle="Não deu para carregar a agenda"
            onRetry={recarregar}
            isEmpty={linhas.length === 0}
            empty={
              <EmptyState
                title={
                  busca !== '' || filtroDeTipo !== null
                    ? 'Nenhum evento encontrado'
                    : `Nada marcado em ${monthName(periodo.month)}`
                }
                description={
                  busca !== '' || filtroDeTipo !== null
                    ? 'Tente outro termo, ou limpe o filtro para ver o mês inteiro.'
                    : 'Cultos, ensaios, reuniões e retiros ficam aqui — visíveis para toda a igreja.'
                }
                {...(busca === '' && filtroDeTipo === null
                  ? {
                      action: (
                        <SignatureButton
                          label="Agendar evento"
                          onPress={() => router.push('/agenda/novo')}
                        />
                      ),
                    }
                  : {})}
              />
            }
          >
            <View style={{ gap: theme.space[8] }}>
              {linhas.map((linha) => (
                <LinhaDeEvento
                  key={linha.evento.id}
                  linha={linha}
                  onApagar={() => confirmarExclusao(linha.evento)}
                />
              ))}
            </View>
          </AsyncContent>

          {linhas.length > 0 && (
            <View
              style={{
                paddingTop: theme.space[12],
                borderTopWidth: 1,
                borderTopColor: theme.colors.hairline,
              }}
            >
              <Text variant="captionBody" tone="muted">
                Mostrando {linhas.length} de {resumo.total}{' '}
                {resumo.total === 1 ? 'evento' : 'eventos'}
              </Text>
            </View>
          )}
        </Card>

        <Text variant="captionBody" tone="muted">
          Congrega · Gestão simples para uma comunidade viva
        </Text>
      </ScrollView>
    </Screen>
  );
}

function CartaoDeResumo({
  icone,
  cor,
  valor,
  rotulo,
  largura,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly cor: string;
  readonly valor: number;
  readonly rotulo: string;
  readonly largura: `${number}%`;
}) {
  const theme = useTheme();

  return (
    <Card style={{ width: largura, flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
      {/* O ícone é reforço: o rótulo escrito ao lado do número é quem diz o que
          ele conta. Escondido do leitor de tela para não duplicar. */}
      <View
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
        style={{
          width: 42,
          height: 42,
          borderRadius: theme.radius.smallCards,
          flexShrink: 0,
          alignItems: 'center',
          justifyContent: 'center',
          // 18% sobre o branco do cartão: fundo suficiente para o ícone ler, e
          // claro o bastante para o número ao lado continuar sendo o que salta.
          backgroundColor: `${cor}2E`,
        }}
      >
        <Feather name={icone} size={18} color={cor} />
      </View>

      <View style={{ flex: 1, minWidth: 0 }}>
        <Text variant="subheading">{valor}</Text>
        <Text variant="captionBody" tone="muted" numberOfLines={1}>
          {rotulo}
        </Text>
      </View>
    </Card>
  );
}

function ChipDeTipo({
  rotulo,
  quantidade,
  cor,
  ativo,
  onPress,
}: {
  readonly rotulo: string;
  readonly quantidade: number;
  readonly cor?: string | null;
  readonly ativo: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="radio"
      accessibilityState={{ checked: ativo }}
      // Rótulo e contagem como UMA frase: separados, o leitor de tela os anuncia
      // como fragmentos sem relação.
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
        backgroundColor: ativo
          ? theme.colors.surfaceAccentSoft
          : pressed
            ? theme.colors.surfaceInner
            : theme.colors.surface,
      })}
    >
      {/* Ponto na cor do tipo. Decorativo: o nome está escrito ao lado, e cor
          sozinha some para quem não distingue matiz. */}
      {cor !== undefined && cor !== null && (
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{ width: 8, height: 8, borderRadius: 4, backgroundColor: cor }}
        />
      )}

      <Text variant="captionBody">{rotulo}</Text>

      <Text variant="caption" tone="muted">
        {quantidade}
      </Text>
    </Pressable>
  );
}

function LinhaDeEvento({
  linha,
  onApagar,
}: {
  readonly linha: Linha;
  readonly onApagar: () => void;
}) {
  const theme = useTheme();
  const { width } = useWindowDimensions();
  const { evento, abreDia } = linha;

  const cancelado = evento.status === 'Cancelado';
  const inicio = new Date(evento.startsAt);
  const emLinha = width >= 620;

  const corDoTipo = evento.type?.colorHex ?? theme.colors.hairline;

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[12],
        paddingVertical: theme.space[8],
        paddingHorizontal: theme.space[8],
        borderRadius: theme.radius.smallCards,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        backgroundColor: theme.colors.surface,
      }}
    >
      {/* O conteúdo é um alvo IRMÃO dos botões, nunca o pai deles.
          Aninhar funcionaria no celular, onde o filho consome o toque, e
          quebraria no navegador, onde o clique sobe: apagar abriria o detalhe do
          evento que acabou de sumir. */}
      <Pressable
        onPress={() => router.push(`/agenda/${evento.id}`)}
        accessibilityRole="link"
        // O rótulo carrega o que a linha comunica visualmente: tipo, data e
        // horário. Sem isso o leitor de tela anunciaria só o título, e a coluna
        // de data vazia nas linhas seguintes do mesmo dia deixaria o evento sem
        // data nenhuma na leitura linear.
        accessibilityLabel={
          `${evento.type === null ? 'Evento sem tipo' : evento.type.name}: ${evento.title}, ` +
          `${capitalizar(FORMATO_SEMANA.format(inicio))} ${FORMATO_DIA.format(inicio)} de ` +
          `${capitalizar(FORMATO_MES.format(inicio))}, às ${formatTime(evento.startsAt)}` +
          (evento.seriesId === null ? '' : ', evento semanal') +
          (cancelado ? ', cancelado' : '')
        }
        style={({ pressed }) => ({
          flex: 1,
          minWidth: 0,
          flexDirection: 'row',
          alignItems: 'center',
          gap: theme.space[12],
          paddingVertical: theme.space[4],
          borderRadius: theme.radius.inputs,
          backgroundColor: pressed ? theme.colors.surfaceInner : 'transparent',
        })}
      >
        {/* Coluna de data com largura fixa: é ela que alinha os títulos numa
            régua só. Largura de conteúdo faria cada linha começar num lugar. */}
        <View style={{ width: LARGURA_DO_BLOCO_DE_DATA, alignItems: 'center', flexShrink: 0 }}>
          {abreDia && (
            <>
              <Text variant="caption" tone="muted">
                {capitalizar(FORMATO_SEMANA.format(inicio))}
              </Text>
              <Text variant="subheading" style={{ opacity: cancelado ? 0.5 : 1 }}>
                {FORMATO_DIA.format(inicio)}
              </Text>
              <Text variant="caption" tone="muted">
                {capitalizar(FORMATO_MES.format(inicio))}
              </Text>
            </>
          )}
        </View>

        {/* A faixa do tipo. Decorativa: a etiqueta com o NOME do tipo está a
            poucos pixels dela, e é a etiqueta que informa. */}
        <View
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
          style={{
            width: 4,
            alignSelf: 'stretch',
            minHeight: 44,
            borderRadius: 2,
            flexShrink: 0,
            backgroundColor: corDoTipo,
            opacity: cancelado ? 0.4 : 1,
          }}
        />

        <View style={{ flex: 1, minWidth: 0, gap: theme.space[4] }}>
          {/* `nowrap` com `minWidth: 0`: com `wrap`, num viewport de 390px o
              título não conseguia encolher e ia inteiro para a linha seguinte,
              deixando a etiqueta sozinha numa linha própria. */}
          <View
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              gap: theme.space[8],
              flexWrap: 'nowrap',
              minWidth: 0,
            }}
          >
            <Text
              variant="bodyStrong"
              numberOfLines={1}
              style={{
                flexShrink: 1,
                minWidth: 0,
                opacity: cancelado ? 0.5 : 1,
                // Riscado comunica o cancelamento sem depender de cor — e o
                // evento continua legível, que é o ponto de não apagá-lo.
                textDecorationLine: cancelado ? 'line-through' : 'none',
              }}
            >
              {evento.title}
            </Text>

            {evento.type !== null && <EtiquetaDeTipo tipo={evento.type} />}

            {/* O selo diz que a linha faz parte de uma série. Sem ele, apagar
                um culto e ver outros cinquenta iguais no lugar pareceria
                defeito. */}
            {evento.seriesId !== null && <EyebrowPill label="Semanal" tone="neutral" />}

            {cancelado && <EyebrowPill label="Cancelado" tone="badge" />}
          </View>

          <View
            style={{
              flexDirection: 'row',
              alignItems: 'center',
              gap: theme.space[12],
              flexWrap: 'wrap',
            }}
          >
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[4] }}>
              <Feather name="clock" size={12} color={theme.colors.textMuted} />
              <Text variant="captionBody" tone="muted">
                {formatTime(evento.startsAt)} – {formatTime(evento.endsAt)}
              </Text>
            </View>

            {evento.location !== null && (
              <View
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: theme.space[4],
                  flexShrink: 1,
                  minWidth: 0,
                }}
              >
                <Feather name="map-pin" size={12} color={theme.colors.textMuted} />
                <Text variant="captionBody" tone="muted" numberOfLines={1} style={{ flexShrink: 1 }}>
                  {evento.location}
                </Text>
              </View>
            )}
          </View>
        </View>
      </Pressable>

      {/* As ações do mockup. Só entram acima de 620px: em 390 elas competiriam
          com o título por uma largura que não existe, e a linha inteira já abre
          o detalhe, onde as duas ações também estão. */}
      {emLinha && (
        <View style={{ flexDirection: 'row', gap: theme.space[8], flexShrink: 0 }}>
          <BotaoDaLinha
            icone="edit-2"
            rotulo={`Editar ${evento.title}`}
            onPress={() => router.push(`/agenda/editar/${evento.id}`)}
          />
          <BotaoDaLinha
            icone="trash-2"
            rotulo={`Apagar ${evento.title}`}
            perigo
            onPress={onApagar}
          />
        </View>
      )}
    </View>
  );
}

function EtiquetaDeTipo({
  tipo,
}: {
  readonly tipo: NonNullable<CalendarEvent['type']>;
}) {
  const theme = useTheme();
  const cor = tipo.colorHex ?? theme.colors.textOnAccentSoft;

  return (
    <View
      style={{
        flexDirection: 'row',
        alignItems: 'center',
        gap: theme.space[4],
        flexShrink: 0,
        paddingVertical: 3,
        paddingHorizontal: theme.space[8],
        borderRadius: theme.radius.tags,
        // Lavagem da própria cor do tipo, a 18%. O texto vai na cor cheia, que
        // já passa em contraste sobre o branco — é a cor que a igreja escolheu.
        backgroundColor: `${cor}2E`,
      }}
    >
      <Feather name={iconeDe(tipo.icon)} size={10} color={cor} />
      <Text variant="caption" style={{ color: cor }}>
        {tipo.name}
      </Text>
    </View>
  );
}

function BotaoDaLinha({
  icone,
  rotulo,
  perigo = false,
  onPress,
}: {
  readonly icone: keyof typeof Feather.glyphMap;
  readonly rotulo: string;
  readonly perigo?: boolean;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={rotulo}
      hitSlop={6}
      style={({ pressed }) => ({
        width: 34,
        height: 34,
        borderRadius: theme.radius.inputs,
        borderWidth: 1,
        borderColor: theme.colors.hairline,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: pressed
          ? perigo
            ? '#FBEAEA'
            : theme.colors.surfaceAccentSoft
          : theme.colors.surfaceInner,
      })}
    >
      <Feather
        name={icone}
        size={15}
        color={perigo ? theme.colors.danger : theme.colors.textOnAccentSoft}
      />
    </Pressable>
  );
}
