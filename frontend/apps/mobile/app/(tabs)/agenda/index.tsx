import type { CalendarEvent } from '@congrega/api-client/events';
import { formatTime, monthName, shiftMonth, type YearMonth } from '@congrega/core/datetime';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { MonthNavigator } from '@congrega/ui/MonthNavigator';
import { Screen } from '@congrega/ui/Screen';
import { SkeletonListRow } from '@congrega/ui/Skeleton';
import { StatCard } from '@congrega/ui/StatCard';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { FlashList } from '@shopify/flash-list';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Pressable, View, useWindowDimensions } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useEventsOfMonth } from '../../../src/useEvents';

function ehMesCorrente(periodo: YearMonth): boolean {
  const atual = mesCorrente();
  return periodo.year === atual.year && periodo.month === atual.month;
}

function mesCorrente(): YearMonth {
  const agora = new Date();
  return { year: agora.getFullYear(), month: agora.getMonth() + 1 };
}

/** `sáb, 22 de agosto` — cabeçalho de um dia da agenda. */
const FORMATO_DE_DIA = new Intl.DateTimeFormat('pt-BR', {
  timeZone: 'America/Sao_Paulo',
  weekday: 'short',
  day: '2-digit',
  month: 'long',
});

type Linha =
  | { readonly tipo: 'dia'; readonly chave: string; readonly rotulo: string }
  | { readonly tipo: 'evento'; readonly chave: string; readonly evento: CalendarEvent };

/**
 * Abaixo disso o cabeçalho empilha: título em cima, período e ação embaixo.
 *
 * O corte é a largura em que título + navegação + botão ainda cabem numa linha
 * sem espremer o rótulo do mês — não é um breakpoint de dispositivo, é a
 * largura em que este conteúdo específico para de caber.
 */
const LARGURA_CABECALHO_EM_LINHA = 720;

export default function Agenda() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { width: larguraJanela } = useWindowDimensions();
  const emColuna = larguraJanela < LARGURA_CABECALHO_EM_LINHA;
  const [periodo, setPeriodo] = useState<YearMonth>(mesCorrente);
  const { eventos, carregando, erro, recarregar } = useEventsOfMonth(periodo);

  useFocusEffect(
    useCallback(() => {
      recarregar();
    }, [recarregar]),
  );

  // Agrupa por dia inserindo cabeçalhos na própria lista, em vez de aninhar
  // uma lista por dia. Lista dentro de lista quebra a virtualização — é o
  // problema que a FlashList existe para evitar.
  const linhas = useMemo<readonly Linha[]>(() => {
    const resultado: Linha[] = [];
    let diaAnterior = '';

    for (const evento of eventos) {
      const dia = FORMATO_DE_DIA.format(new Date(evento.startsAt));
      if (dia !== diaAnterior) {
        resultado.push({ tipo: 'dia', chave: `dia-${dia}`, rotulo: dia });
        diaAnterior = dia;
      }
      resultado.push({ tipo: 'evento', chave: evento.id, evento });
    }

    return resultado;
  }, [eventos]);

  /**
   * Resumo do mês — **só o que os dados sustentam**.
   *
   * O mockup pede "4 Reuniões · 3 Cultos · 1 Ensaio", e `events` não tem coluna
   * de tipo: nem no banco, nem no contrato da API. Preencher isso exigiria
   * adivinhar a categoria pelo título, que é exatamente o dado falso mascarando
   * API inexistente que o próprio brief proíbe (§20) — e erraria em "Culto de
   * Oração", que é as duas coisas.
   *
   * Contagem, cancelados e próximo evento saem de `startsAt` e `status`, que
   * existem. Quando houver um campo de tipo, a divisão por categoria entra aqui
   * sem mexer no resto da tela.
   */
  const resumo = useMemo(() => {
    const agora = Date.now();
    const cancelados = eventos.filter((e) => e.status === 'Cancelado').length;
    const proximo = eventos.find(
      (e) => e.status !== 'Cancelado' && new Date(e.startsAt).getTime() >= agora,
    );

    return { total: eventos.length, cancelados, proximo };
  }, [eventos]);

  return (
    <Screen padded={false} wide>
      <View
        style={{
          paddingTop: insets.top + theme.space[16],
          paddingHorizontal: theme.space[24],
          gap: theme.space[16],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        {/* Título, período e ação na MESMA faixa quando há largura para isso.
            Antes o botão de agendar era uma barra lima da largura inteira, e uma
            faixa saturada de 1200px sobre uma lista discreta rouba a hierarquia
            do conteúdo que a tela existe para mostrar. Como ação secundária ao
            lado do período, ele continua acessível sem competir. */}
        <View
          style={{
            flexDirection: emColuna ? 'column' : 'row',
            alignItems: emColuna ? 'stretch' : 'flex-end',
            justifyContent: 'space-between',
            gap: theme.space[16],
          }}
        >
          <View style={{ gap: theme.space[4] }}>
            <Text variant="eyebrow" tone="muted">
              SUA IGREJA
            </Text>
            <Text variant="heading">Agenda</Text>
          </View>

          {/* Em coluna o período e a ação também empilham: lado a lado eles somam
              mais de 400px, e num viewport de 390px o botão saía pela borda —
              cortado, mas ainda clicável, que é a pior combinação. */}
          <View
            style={{
              flexDirection: emColuna ? 'column' : 'row',
              alignItems: emColuna ? 'stretch' : 'center',
              justifyContent: 'flex-end',
              gap: theme.space[12],
            }}
          >
            <MonthNavigator
              label={`${monthName(periodo.month)} de ${periodo.year}`}
              onChange={(passos) => setPeriodo((a) => shiftMonth(a, passos))}
              onToday={() => setPeriodo(mesCorrente())}
              isCurrentMonth={ehMesCorrente(periodo)}
              {...(emColuna ? { style: { alignSelf: 'center' } } : {})}
            />

            {eventos.length > 0 && (
              <Button label="Agendar evento" variant="outline" onPress={() => router.push('/agenda/novo')} />
            )}
          </View>
        </View>

        {/* Resumo só quando há o que resumir: uma faixa de zeros sobre uma
            agenda vazia ocupa espaço para não dizer nada, e o estado vazio
            abaixo já explica a situação melhor. */}
        {eventos.length > 0 && (
          <View style={{ flexDirection: 'row', gap: theme.space[12], flexWrap: 'wrap' }}>
            <StatCard
              compact
              style={{ flex: 1, minWidth: 150 }}
              value={String(resumo.total)}
              label={resumo.total === 1 ? 'evento neste mês' : 'eventos neste mês'}
            />

            {resumo.proximo !== undefined && (
              <StatCard
                compact
                style={{ flex: 1, minWidth: 150 }}
                value={formatTime(resumo.proximo.startsAt)}
                label={`próximo · ${resumo.proximo.title}`}
              />
            )}

            {resumo.cancelados > 0 && (
              <StatCard
                compact
                style={{ flex: 1, minWidth: 150 }}
                value={String(resumo.cancelados)}
                label={resumo.cancelados === 1 ? 'cancelado' : 'cancelados'}
              />
            )}
          </View>
        )}
      </View>

      <AsyncContent
        fill
        loading={carregando}
        skeleton={
          <View
            style={{
              paddingHorizontal: theme.space[24],
              paddingTop: theme.space[16],
              gap: theme.space[8],
              maxWidth: theme.layout.pageMaxWidth,
              width: '100%',
              alignSelf: 'center',
            }}
          >
            <SkeletonListRow />
            <SkeletonListRow />
            <SkeletonListRow />
          </View>
        }
        failure={erro}
        errorTitle="Não deu para carregar a agenda"
        onRetry={recarregar}
        isEmpty={eventos.length === 0}
        empty={
          <EmptyState
            title={`Nada marcado em ${monthName(periodo.month)}`}
            description="Cultos, ensaios, reuniões e retiros ficam aqui — visíveis para toda a igreja."
            action={<SignatureButton label="Agendar evento" onPress={() => router.push('/agenda/novo')} />}
          />
        }
      >
        <FlashList
          data={linhas}
          keyExtractor={(item) => item.chave}
          renderItem={({ item }) =>
            item.tipo === 'dia' ? (
              <Text
                variant="eyebrow"
                tone="muted"
                style={{ paddingTop: theme.space[16], paddingBottom: theme.space[4] }}
              >
                {item.rotulo.toUpperCase()}
              </Text>
            ) : (
              <LinhaDeEvento evento={item.evento} />
            )
          }
          contentContainerStyle={{
            paddingHorizontal: theme.space[24],
            paddingBottom: insets.bottom + theme.space[24],
            maxWidth: theme.layout.pageMaxWidth,
            width: '100%',
            alignSelf: 'center',
          }}
          ItemSeparatorComponent={() => <View style={{ height: theme.space[8] }} />}
        />
      </AsyncContent>
    </Screen>
  );
}

function LinhaDeEvento({ evento }: { readonly evento: CalendarEvent }) {
  const theme = useTheme();
  const cancelado = evento.status === 'Cancelado';

  return (
    <Pressable
      onPress={() => router.push(`/agenda/${evento.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Abrir ${evento.title}`}
      style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
    >
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
          <View style={{ gap: 2, minWidth: 52 }}>
            <Text variant="bodyStrong" style={{ opacity: cancelado ? 0.5 : 1 }}>
              {formatTime(evento.startsAt)}
            </Text>
            <Text variant="captionBody" tone="muted">
              {formatTime(evento.endsAt)}
            </Text>
          </View>

          <View
            style={{
              width: 1,
              alignSelf: 'stretch',
              backgroundColor: theme.colors.hairline,
            }}
          />

          <View style={{ flex: 1, gap: theme.space[4] }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8], flexWrap: 'wrap' }}>
              <Text
                variant="bodyStrong"
                style={{
                  flexShrink: 1,
                  opacity: cancelado ? 0.5 : 1,
                  // Riscado é o que comunica cancelamento sem depender de cor —
                  // e o evento continua legível, que é o ponto de não apagá-lo.
                  textDecorationLine: cancelado ? 'line-through' : 'none',
                }}
                numberOfLines={1}
              >
                {evento.title}
              </Text>
              {cancelado && <EyebrowPill label="Cancelado" tone="badge" />}
            </View>

            {/* Ícone de local, do mesmo conjunto Feather usado no resto do app.
                O §18 do brief pede um sistema só de ícones, e misturar emoji
                aqui — como o mockup sugere — quebraria isso: emoji renderiza
                com a fonte do sistema e muda de forma entre plataformas. */}
            {evento.location !== null && (
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[4] }}>
                <Feather name="map-pin" size={12} color={theme.colors.textMuted} />
                <Text variant="captionBody" tone="muted" numberOfLines={1} style={{ flexShrink: 1 }}>
                  {evento.location}
                </Text>
              </View>
            )}
          </View>
        </View>
      </Card>
    </Pressable>
  );
}
