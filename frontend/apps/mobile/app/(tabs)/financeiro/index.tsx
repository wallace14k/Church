import { deleteGivingEntry, type GivingEntry } from '@congrega/api-client/giving';
import { formatDate } from '@congrega/core/datetime';
import { cents, formatBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { MonthNavigator } from '@congrega/ui/MonthNavigator';
import { Button } from '@congrega/ui/Button';
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
import { useCallback, useState } from 'react';
import { Alert, Platform, Pressable, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiClient } from '../../../src/api';
import { deslocarMes, mesCorrente, nomeDoMes, useGivingEntries, useMonthlyClosing } from '../../../src/useGiving';

export default function Financeiro() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const [periodo, setPeriodo] = useState(mesCorrente);

  const { lancamentos, carregando, erro, recarregar } = useGivingEntries(periodo.year, periodo.month);
  const { fechamento, recarregar: recarregarFechamento } = useMonthlyClosing(periodo.year, periodo.month);

  // Voltar da tela de lançar precisa refletir o que acabou de ser lançado. Sem
  // isso, o tesoureiro digita, volta e não vê — e lança de novo.
  useFocusEffect(
    useCallback(() => {
      recarregar();
      recarregarFechamento();
    }, [recarregar, recarregarFechamento]),
  );

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

  function confirmarExclusao(lancamento: GivingEntry) {
    const descricao = `${lancamento.categoryName} · ${formatBRL(cents(lancamento.amountCents))}`;

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

  const saldo = fechamento?.balanceCents ?? 0;

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
          <Text variant="heading">Financeiro</Text>
        </View>

        <MonthNavigator
          label={`${nomeDoMes(periodo.month)} de ${periodo.year}`}
          onChange={(passos) => setPeriodo((atual) => deslocarMes(atual, passos))}
        />

        {fechamento !== null && (
          <Pressable
            onPress={() => router.push('/financeiro/fechamento')}
            accessibilityRole="button"
            accessibilityLabel="Ver fechamento do mês"
            style={({ pressed }) => ({ opacity: pressed ? 0.75 : 1 })}
          >
            <Card>
              <View style={{ flexDirection: 'row', justifyContent: 'space-between', gap: theme.space[12] }}>
                <Totalzinho rotulo="ENTRADAS" valorCents={fechamento.totalIncomeCents} />
                <Totalzinho rotulo="SAÍDAS" valorCents={fechamento.totalExpenseCents} />
                <Totalzinho
                  rotulo="SALDO"
                  valorCents={saldo}
                  // Saldo negativo é a informação mais importante da tela: é o
                  // mês em que a igreja gastou mais do que arrecadou.
                  cor={saldo < 0 ? theme.colors.danger : theme.colors.text}
                />
              </View>
            </Card>
          </Pressable>
        )}

        <View style={{ flexDirection: 'row', gap: theme.space[8] }}>
          <View style={{ flex: 1 }}>
            <SignatureButton label="Lançar" onPress={() => router.push('/financeiro/lancar')} />
          </View>
          <Button label="Categorias" onPress={() => router.push('/financeiro/categorias')} />
        </View>
      </View>

      <AsyncContent
        fill
        loading={carregando}
        failure={erro}
        errorTitle="Não deu para carregar o caixa"
        onRetry={recarregar}
        isEmpty={lancamentos.length === 0}
        empty={
          <EmptyState
            title={`Nenhum lançamento em ${nomeDoMes(periodo.month)}`}
            description="Registre as entradas e saídas do mês para fechar as contas sem planilha."
            action={<SignatureButton label="Lançar" onPress={() => router.push('/financeiro/lancar')} />}
          />
        }
      >
        <FlashList
          data={lancamentos}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <LinhaDeLancamento lancamento={item} onApagar={() => confirmarExclusao(item)} />
          )}
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

function Totalzinho({
  rotulo,
  valorCents,
  cor,
}: {
  readonly rotulo: string;
  readonly valorCents: number;
  readonly cor?: string;
}) {
  const theme = useTheme();

  return (
    <View style={{ gap: 2, flex: 1 }}>
      <Text variant="eyebrow" tone="muted">
        {rotulo}
      </Text>
      <Text variant="bodyStrong" style={{ color: cor ?? theme.colors.text }} numberOfLines={1}>
        {formatBRL(cents(valorCents))}
      </Text>
    </View>
  );
}

function LinhaDeLancamento({
  lancamento,
  onApagar,
}: {
  readonly lancamento: GivingEntry;
  readonly onApagar: () => void;
}) {
  const theme = useTheme();
  const isSaida = lancamento.kind === 'Saida';

  const detalhes = [
    formatDate(`${lancamento.occurredOn}T12:00:00Z`),
    lancamento.method,
    lancamento.memberName,
  ].filter(Boolean);

  return (
    <Card>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}>
        <View style={{ flex: 1, gap: theme.space[4] }}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[8] }}>
            <Text variant="bodyStrong" style={{ flexShrink: 1 }} numberOfLines={1}>
              {lancamento.categoryName}
            </Text>
            {isSaida && <EyebrowPill label="Saída" tone="badge" />}
          </View>
          <Text variant="captionBody" tone="muted" numberOfLines={1}>
            {detalhes.join(' · ')}
          </Text>
        </View>

        <Text
          variant="bodyStrong"
          style={{ color: isSaida ? theme.colors.danger : theme.colors.text }}
        >
          {isSaida ? '−' : ''}
          {formatBRL(cents(lancamento.amountCents))}
        </Text>

        <Pressable
          onPress={onApagar}
          accessibilityRole="button"
          accessibilityLabel={`Apagar lançamento de ${lancamento.categoryName}`}
          hitSlop={8}
          style={({ pressed }) => ({ opacity: pressed ? 0.5 : 1, padding: theme.space[4] })}
        >
          <Feather name="trash-2" size={16} color={theme.colors.textMuted} />
        </Pressable>
      </View>
    </Card>
  );
}
