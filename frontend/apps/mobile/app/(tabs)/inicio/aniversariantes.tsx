import type { Member } from '@congrega/api-client/members';
import { formatBirthday } from '@congrega/core/datetime';
import { Avatar } from '@congrega/ui/Avatar';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { ActivityIndicator, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAniversariantes } from '../../../src/useAniversariantes';

const MESES = [
  'janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho',
  'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro',
] as const;

export default function Aniversariantes() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const mesAtual = new Date().getMonth() + 1;
  const { membros, total, carregando, carregandoMais, erro, temMais, carregarMais } =
    useAniversariantes(mesAtual);

  return (
    <Screen padded={false} wide>
      <View
        style={{
          paddingTop: insets.top + theme.space[16],
          paddingHorizontal: theme.space[24],
          gap: theme.space[4],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <Pressable
          onPress={() => router.back()}
          accessibilityRole="button"
          accessibilityLabel="Voltar"
          hitSlop={8}
          style={({ pressed }) => ({
            width: theme.touch.minTarget,
            height: theme.touch.minTarget,
            marginLeft: -theme.space[8],
            alignItems: 'flex-start',
            justifyContent: 'center',
            opacity: pressed ? 0.6 : 1,
          })}
        >
          <Feather name="chevron-left" size={26} color={theme.colors.text} />
        </Pressable>

        <Text variant="eyebrow" tone="muted">
          SUA IGREJA
        </Text>
        <Text variant="heading">Aniversariantes de {MESES[mesAtual - 1]}</Text>
      </View>

      {carregando ? (
        <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
          <ActivityIndicator color={theme.colors.text} />
        </View>
      ) : erro !== null ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[32] }}>
          <EmptyState title="Não deu para carregar a lista" description={erro} />
        </View>
      ) : membros.length === 0 ? (
        <View style={{ paddingHorizontal: theme.space[24], paddingTop: theme.space[16] }}>
          <EmptyState
            title="Ninguém faz aniversário este mês"
            description="Assim que alguém com data de nascimento cadastrada fizer aniversário neste mês, aparece aqui."
          />
        </View>
      ) : (
        <FlashList
          data={membros}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <LinhaDeAniversariante membro={item} />}
          contentContainerStyle={{
            paddingHorizontal: theme.space[24],
            paddingTop: theme.space[16],
            paddingBottom: insets.bottom + theme.space[24],
            maxWidth: theme.layout.pageMaxWidth,
            width: '100%',
            alignSelf: 'center',
          }}
          ItemSeparatorComponent={() => <View style={{ height: theme.space[8] }} />}
          onEndReached={carregarMais}
          onEndReachedThreshold={0.6}
          ListFooterComponent={
            carregandoMais ? (
              <View style={{ paddingVertical: theme.space[24] }}>
                <ActivityIndicator color={theme.colors.text} />
              </View>
            ) : temMais ? null : (
              <Text
                variant="captionBody"
                tone="muted"
                style={{ textAlign: 'center', paddingVertical: theme.space[24] }}
              >
                {total === 1 ? '1 aniversariante' : `${total} aniversariantes`}
              </Text>
            )
          }
        />
      )}
    </Screen>
  );
}

function LinhaDeAniversariante({ membro }: { readonly membro: Member }) {
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

          <View style={{ flex: 1, gap: theme.space[4] }}>
            <Text variant="bodyStrong" numberOfLines={1}>
              {membro.fullName}
            </Text>
            {membro.birthDate !== null && (
              <Text variant="captionBody" tone="muted">
                {formatBirthday(membro.birthDate)}
                {descreverIdade(membro.birthDate, membro.age)}
              </Text>
            )}
          </View>
        </View>
      </Card>
    </Pressable>
  );
}

/**
 * "· completa 42 anos" ou "· completou 42 anos", conforme o dia já passou ou
 * não neste mês.
 *
 * `age` da API é a idade calculada em relação a hoje — já inclui o aniversário
 * deste ano se ele já aconteceu. Usar sempre "completa {age + 1}" erraria por
 * um ano em quem já fez aniversário há dias, dentro do mesmo mês.
 */
function descreverIdade(birthDateIso: string, age: number | null): string {
  if (age === null) return '';

  const partes = /^\d{4}-\d{2}-(\d{2})/u.exec(birthDateIso);
  if (partes === null) return '';

  const diaAniversario = Number(partes[1]);
  const hoje = new Date().getDate();

  return diaAniversario >= hoje ? ` · completa ${age + 1} anos` : ` · completou ${age} anos`;
}
