import { describeError } from '@congrega/api-client/errors';
import { getFamily, type FamilyDetail, type FamilyMember } from '@congrega/api-client/families';
import { Avatar } from '@congrega/ui/Avatar';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
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
import { apiClient } from '../../../../src/api';

export default function FichaDeFamilia() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [familia, setFamilia] = useState<FamilyDetail | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);

  useEffect(() => {
    let cancelado = false;

    async function carregar() {
      try {
        const encontrada = await getFamily(apiClient, id ?? '');
        if (!cancelado) setFamilia(encontrada);
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

  if (erro !== null || familia === null) {
    return (
      <Screen wide>
        <View style={{ paddingTop: insets.top + theme.space[16] }}>{voltar}</View>
        <View style={{ paddingTop: theme.space[32], maxWidth: 480 }}>
          <EmptyState
            title="Família não encontrada"
            description={erro ?? 'Esta família não existe ou não pertence à sua igreja.'}
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

        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            FAMÍLIA
          </Text>
          <Text variant="heading">{familia.name}</Text>
        </View>

        {familia.members.length === 0 ? (
          <EmptyState
            title="Ninguém vinculado ainda"
            description="Abra a ficha de um membro e escolha esta família para agrupá-lo aqui."
          />
        ) : (
          <View style={{ gap: theme.space[8] }}>
            {familia.members.map((membro: FamilyMember) => (
              <LinhaDeMembro key={membro.id} membro={membro} />
            ))}
          </View>
        )}
      </ScrollView>
    </Screen>
  );
}

function LinhaDeMembro({ membro }: { readonly membro: FamilyMember }) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={() => router.push(`/membros/${membro.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Abrir ficha de ${membro.fullName}`}
      style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
    >
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
          <Avatar name={membro.fullName} />
          <Text variant="bodyStrong" style={{ flex: 1 }} numberOfLines={1}>
            {membro.fullName}
          </Text>
          {membro.status !== 'Ativo' && <EyebrowPill label={membro.status} tone="badge" />}
        </View>
      </Card>
    </Pressable>
  );
}
