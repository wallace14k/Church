import type { CalendarEvent } from '@congrega/api-client/events';
import { formatTime, monthName, shiftMonth, type YearMonth } from '@congrega/core/datetime';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { FlashList } from '@shopify/flash-list';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { ActivityIndicator, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useEventsOfMonth } from '../../../src/useEvents';

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

export default function Agenda() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
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
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            SUA IGREJA
          </Text>
          <Text variant="heading">Agenda</Text>
        </View>

        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
          <Seta direcao="chevron-left" rotulo="Mês anterior" onPress={() => setPeriodo((a) => shiftMonth(a, -1))} />
          <Text variant="bodyStrong">
            {monthName(periodo.month)} de {periodo.year}
          </Text>
          <Seta direcao="chevron-right" rotulo="Próximo mês" onPress={() => setPeriodo((a) => shiftMonth(a, 1))} />
        </View>

        {eventos.length > 0 && (
          <SignatureButton label="Agendar evento" onPress={() => router.push('/agenda/novo')} />
        )}
      </View>

      {carregando ? (
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
          <ActivityIndicator color={theme.colors.text} />
        </View>
      ) : erro !== null ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[32] }}>
          <EmptyState
            title="Não deu para carregar a agenda"
            description={erro}
            action={<SignatureButton label="Tentar de novo" onPress={recarregar} />}
          />
        </View>
      ) : eventos.length === 0 ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[16] }}>
          <EmptyState
            title={`Nada marcado em ${monthName(periodo.month)}`}
            description="Cultos, ensaios, reuniões e retiros ficam aqui — visíveis para toda a igreja."
            action={<SignatureButton label="Agendar evento" onPress={() => router.push('/agenda/novo')} />}
          />
        </View>
      ) : (
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
      )}
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
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
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

            {evento.location !== null && (
              <Text variant="captionBody" tone="muted" numberOfLines={1}>
                {evento.location}
              </Text>
            )}
          </View>
        </View>
      </Card>
    </Pressable>
  );
}

function Seta({
  direcao,
  rotulo,
  onPress,
}: {
  readonly direcao: 'chevron-left' | 'chevron-right';
  readonly rotulo: string;
  readonly onPress: () => void;
}) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={rotulo}
      hitSlop={8}
      style={({ pressed }) => ({
        width: theme.touch.minTarget,
        height: theme.touch.minTarget,
        alignItems: 'center',
        justifyContent: 'center',
        opacity: pressed ? 0.6 : 1,
      })}
    >
      <Feather name={direcao} size={22} color={theme.colors.text} />
    </Pressable>
  );
}
