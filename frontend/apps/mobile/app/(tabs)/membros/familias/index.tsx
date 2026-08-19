import type { Family } from '@congrega/api-client/families';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useFamilies } from '../../../../src/useFamilies';

export default function ListaDeFamilias() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { familias, carregando, erro, recarregar } = useFamilies();

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
          <Text variant="heading">Famílias</Text>
        </View>

        {familias.length > 0 && (
          <SignatureButton label="Nova família" onPress={() => router.push('/membros/familias/nova')} />
        )}
      </View>

      <AsyncContent
        fill
        loading={carregando}
        failure={erro}
        errorTitle="Não deu para carregar as famílias"
        onRetry={recarregar}
        isEmpty={familias.length === 0}
        empty={
          <EmptyState
            title="Nenhuma família cadastrada"
            description="Agrupe membros da mesma família para localizá-los juntos na ficha de cada um."
            action={
              <SignatureButton label="Nova família" onPress={() => router.push('/membros/familias/nova')} />
            }
          />
        }
      >
        <FlashList
          data={familias}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <LinhaDeFamilia familia={item} />}
          contentContainerStyle={{
            paddingHorizontal: theme.space[24],
            paddingTop: theme.space[16],
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

function LinhaDeFamilia({ familia }: { readonly familia: Family }) {
  const theme = useTheme();

  return (
    <Pressable
      onPress={() => router.push(`/membros/familias/${familia.id}`)}
      accessibilityRole="button"
      accessibilityLabel={`Abrir família ${familia.name}`}
      style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
    >
      <Card>
        <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
          <Text variant="bodyStrong">{familia.name}</Text>
          <EyebrowPill
            label={familia.memberCount === 1 ? '1 pessoa' : `${familia.memberCount} pessoas`}
            tone="badge"
          />
        </View>
      </Card>
    </Pressable>
  );
}
