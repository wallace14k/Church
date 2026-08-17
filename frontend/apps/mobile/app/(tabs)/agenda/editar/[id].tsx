import { describeError } from '@congrega/api-client/errors';
import { getEvent, updateEvent, type CalendarEvent } from '@congrega/api-client/events';
import { Button } from '@congrega/ui/Button';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { useTheme } from '@congrega/ui/theme';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../../src/api';
import { FormularioDeEvento } from '../../../../src/FormularioDeEvento';

export default function EditarEvento() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [evento, setEvento] = useState<CalendarEvent | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);

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

  if (carregando) {
    return (
      <Screen wide style={{ alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color={theme.colors.text} />
      </Screen>
    );
  }

  if (erro !== null || evento === null) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Evento não encontrado"
            description={erro ?? 'Este evento não existe ou não pertence à sua igreja.'}
            action={<Button label="Voltar" onPress={() => router.back()} />}
          />
        </View>
      </Screen>
    );
  }

  return (
    <FormularioDeEvento
      eyebrow="EDITAR"
      titulo={evento.title}
      inicial={evento}
      onSalvar={async (entrada) => {
        await updateEvent(apiClient, id ?? '', entrada);
        router.replace(`/agenda/${id}`);
      }}
    />
  );
}
