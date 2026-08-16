import { describeError } from '@congrega/api-client/errors';
import { getMember, type Member } from '@congrega/api-client/members';
import { formatPhone } from '@congrega/core/validation';
import { formatDate } from '@congrega/core/datetime';
import { Avatar } from '@congrega/ui/Avatar';
import { Card } from '@congrega/ui/Card';
import { Button } from '@congrega/ui/Button';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';

export default function FichaDeMembro() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [membro, setMembro] = useState<Member | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);

  useEffect(() => {
    let cancelado = false;

    async function carregar() {
      try {
        const encontrado = await getMember(apiClient, id ?? '');
        if (!cancelado) setMembro(encontrado);
      } catch (causa) {
        if (!cancelado) setErro(describeError(causa));
      } finally {
        if (!cancelado) setCarregando(false);
      }
    }

    void carregar();
    return () => {
      cancelado = true;
    };
  }, [id]);

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

  if (erro !== null || membro === null) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[16] }}>{voltar}</View>
        <View style={{ paddingTop: theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Ficha não encontrada"
            description={erro ?? 'Este membro não existe ou não pertence à sua igreja.'}
            action={<Button label="Voltar" onPress={() => router.back()} />}
          />
        </View>
      </Screen>
    );
  }

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

        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[16] }}>
          <Avatar name={membro.fullName} size={64} />
          <View style={{ flex: 1, gap: theme.space[4] }}>
            {membro.status !== 'Ativo' && <EyebrowPill label={membro.status} tone="badge" />}
            <Text variant="heading">{membro.fullName}</Text>
          </View>
        </View>

        <Card>
          <Campo rotulo="E-mail" valor={membro.email} />
          <Campo rotulo="Telefone" valor={formatPhone(membro.phone)} />
          <Campo
            rotulo="Nascimento"
            valor={
              membro.birthDate
                ? `${formatDate(membro.birthDate)}${membro.age !== null ? ` · ${membro.age} anos` : ''}`
                : null
            }
          />
          <Campo rotulo="Família" valor={membro.familyName} />
        </Card>

        <Button label="Editar membro" onPress={() => router.push(`/membros/editar/${membro.id}`)} />

        <Text variant="captionBody" tone="muted">
          Histórico de contribuições e presença chegam nas próximas etapas.
        </Text>
      </ScrollView>
    </Screen>
  );
}

/**
 * Linha de rótulo e valor.
 *
 * Campo vazio some em vez de mostrar traço: uma ficha cheia de "—" parece
 * cadastro malfeito, quando na verdade a secretaria só não tinha aquele dado.
 */
function Campo({ rotulo, valor }: { readonly rotulo: string; readonly valor: string | null }) {
  const theme = useTheme();

  if (!valor) return null;

  return (
    <View style={{ gap: 2, paddingVertical: theme.space[4] }}>
      <Text variant="eyebrow" tone="muted">{rotulo.toUpperCase()}</Text>
      <Text variant="body">{valor}</Text>
    </View>
  );
}
