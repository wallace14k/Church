import { describeError } from '@congrega/api-client/errors';
import {
  cancelEvent,
  deleteEvent,
  getEvent,
  reactivateEvent,
  type CalendarEvent,
} from '@congrega/api-client/events';
import { formatTime, formatWeekday } from '@congrega/core/datetime';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Platform, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';

export default function FichaDeEvento() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [evento, setEvento] = useState<CalendarEvent | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [agindo, setAgindo] = useState(false);

  useEffect(() => {
    let cancelado = false;

    getEvent(apiClient, id ?? '')
      .then((encontrado) => {
        if (!cancelado) setEvento(encontrado);
      })
      .catch((causa: unknown) => {
        if (!cancelado) setErro(describeError(causa));
      })
      .finally(() => {
        if (!cancelado) setCarregando(false);
      });

    return () => {
      cancelado = true;
    };
  }, [id]);

  async function alternarCancelamento() {
    if (evento === null) return;

    setAgindo(true);
    try {
      const atualizado =
        evento.status === 'Cancelado'
          ? await reactivateEvent(apiClient, evento.id)
          : await cancelEvent(apiClient, evento.id);
      setEvento(atualizado);
    } catch (causa) {
      setErro(describeError(causa));
    } finally {
      setAgindo(false);
    }
  }

  async function apagar() {
    if (evento === null) return;

    setAgindo(true);
    try {
      await deleteEvent(apiClient, evento.id);
      router.replace('/agenda');
    } catch (causa) {
      setErro(describeError(causa));
      setAgindo(false);
    }
  }

  function confirmarExclusao() {
    if (evento === null) return;

    const mensagem =
      `Apagar "${evento.title}" da agenda? ` +
      'Para avisar quem já sabia, prefira cancelar — o evento continua visível, marcado como cancelado.';

    if (Platform.OS === 'web') {
      // eslint-disable-next-line no-alert
      if (globalThis.confirm(mensagem)) void apagar();
      return;
    }

    Alert.alert('Apagar evento', mensagem, [
      { text: 'Cancelar', style: 'cancel' },
      { text: 'Apagar', style: 'destructive', onPress: () => void apagar() },
    ]);
  }

  const voltar = (
    <Pressable
      onPress={() => router.back()}
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

  if (carregando) {
    return (
      <Screen wide style={{ alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color={theme.colors.text} />
      </Screen>
    );
  }

  if (evento === null) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[16] }}>{voltar}</View>
        <View style={{ paddingTop: theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Evento não encontrado"
            description={erro ?? 'Este evento não existe ou não pertence à sua igreja.'}
            action={<Button label="Voltar" onPress={() => router.back()} />}
          />
        </View>
      </Screen>
    );
  }

  const cancelado = evento.status === 'Cancelado';

  return (
    <Screen padded={false} wide>
      <ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[8],
          paddingHorizontal: theme.space[24],
          paddingBottom: insets.bottom + theme.space[32],
          gap: theme.space[20],
          maxWidth: 720,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        {voltar}

        <View style={{ gap: theme.space[8] }}>
          {cancelado && <EyebrowPill label="Cancelado" tone="badge" />}
          <Text
            variant="heading"
            style={{ textDecorationLine: cancelado ? 'line-through' : 'none' }}
          >
            {evento.title}
          </Text>
        </View>

        <Card>
          <Campo rotulo="Quando" valor={descreverQuando(evento)} />
          <Campo rotulo="Local" valor={evento.location} />
          <Campo rotulo="Descrição" valor={evento.description} />
        </Card>

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

        <SignatureButton
          label="Editar evento"
          onPress={() => router.push(`/agenda/editar/${evento.id}`)}
        />

        <View
          style={{
            marginTop: theme.space[8],
            paddingTop: theme.space[16],
            borderTopWidth: 1,
            borderTopColor: theme.colors.hairline,
            gap: theme.space[8],
          }}
        >
          <Text variant="captionBody" tone="muted">
            {cancelado
              ? 'Reativar devolve o evento à agenda como confirmado.'
              : 'Cancelar mantém o evento na agenda, marcado — é assim que quem já sabia fica sabendo.'}
          </Text>
          <Button
            label={cancelado ? 'Reativar evento' : 'Cancelar evento'}
            variant="outline"
            loading={agindo}
            onPress={() => void alternarCancelamento()}
          />
          <Button label="Apagar da agenda" variant="ghost" onPress={confirmarExclusao} />
        </View>
      </ScrollView>
    </Screen>
  );
}

/** `sábado, 16 de agosto · 19:00 às 21:00`, ou com as duas datas se cruzar o dia. */
function descreverQuando(evento: CalendarEvent): string {
  const inicio = formatWeekday(evento.startsAt);
  const fim = formatWeekday(evento.endsAt);
  const horas = `${formatTime(evento.startsAt)} às ${formatTime(evento.endsAt)}`;

  return inicio === fim ? `${inicio} · ${horas}` : `${inicio}, ${formatTime(evento.startsAt)} até ${fim}, ${formatTime(evento.endsAt)}`;
}

function Campo({ rotulo, valor }: { readonly rotulo: string; readonly valor: string | null }) {
  const theme = useTheme();

  if (!valor) return null;

  return (
    <View style={{ gap: 2, paddingVertical: theme.space[4] }}>
      <Text variant="eyebrow" tone="muted">
        {rotulo.toUpperCase()}
      </Text>
      <Text variant="body">{valor}</Text>
    </View>
  );
}
