import type { ClosingLine } from '@congrega/api-client/giving';
import { cents, formatBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { MonthNavigator } from '@congrega/ui/MonthNavigator';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { Feather } from '@expo/vector-icons';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { deslocarMes, mesCorrente, nomeDoMes, useMonthlyClosing } from '../../../src/useGiving';

export default function FechamentoDoMes() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const [periodo, setPeriodo] = useState(mesCorrente);
  const { fechamento, carregando, erro, recarregar } = useMonthlyClosing(periodo.year, periodo.month);

  const entradas = fechamento?.lines.filter((l) => l.kind === 'Entrada') ?? [];
  const saidas = fechamento?.lines.filter((l) => l.kind === 'Saida') ?? [];
  const saldo = fechamento?.balanceCents ?? 0;

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
            PRESTAÇÃO DE CONTAS
          </Text>
          <Text variant="heading">Fechamento do mês</Text>
        </View>

        <MonthNavigator
          label={`${nomeDoMes(periodo.month)} de ${periodo.year}`}
          onChange={(passos) => setPeriodo((a) => deslocarMes(a, passos))}
        />

        <AsyncContent
          loading={carregando}
          failure={erro}
          errorTitle="Não deu para carregar o fechamento"
          onRetry={recarregar}
          isEmpty={fechamento === null || fechamento.lines.length === 0}
          empty={
            <EmptyState
              title={`Nada lançado em ${nomeDoMes(periodo.month)}`}
              description="Sem lançamentos no mês não há o que fechar. Registre as entradas e saídas primeiro."
              action={<SignatureButton label="Lançar" onPress={() => router.push('/financeiro/lancar')} />}
            />
          }
        >
          {/* O `isEmpty` acima já cobre `fechamento === null`, mas o
              estreitamento de tipo não atravessa a fronteira do componente —
              o compilador precisa ver a checagem aqui dentro. */}
          {fechamento !== null && (
            <>
              <Card>
                <View style={{ gap: theme.space[8] }}>
                  <LinhaDeTotal rotulo="Total de entradas" valorCents={fechamento.totalIncomeCents} />
                  <LinhaDeTotal
                    rotulo="Total de saídas"
                    valorCents={fechamento.totalExpenseCents}
                    cor={theme.colors.danger}
                    prefixo="−"
                  />
                  <View
                    style={{
                      height: 1,
                      backgroundColor: theme.colors.hairline,
                      marginVertical: theme.space[4],
                    }}
                  />
                  <LinhaDeTotal
                    rotulo="Saldo do mês"
                    valorCents={saldo}
                    cor={saldo < 0 ? theme.colors.danger : theme.colors.text}
                    forte
                  />
                </View>
              </Card>

              {entradas.length > 0 && <Grupo titulo="ENTRADAS" linhas={entradas} />}
              {saidas.length > 0 && <Grupo titulo="SAÍDAS" linhas={saidas} negativo />}
            </>
          )}
        </AsyncContent>
      </ScrollView>
    </Screen>
  );
}

function Grupo({
  titulo,
  linhas,
  negativo = false,
}: {
  readonly titulo: string;
  readonly linhas: readonly ClosingLine[];
  readonly negativo?: boolean;
}) {
  const theme = useTheme();

  return (
    <View style={{ gap: theme.space[8] }}>
      <Text variant="eyebrow" tone="muted">
        {titulo}
      </Text>
      <Card>
        <View style={{ gap: theme.space[12] }}>
          {linhas.map((linha) => (
            <View
              key={linha.categoryId}
              style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}
            >
              <View style={{ flex: 1, gap: 2 }}>
                <Text variant="body" numberOfLines={1}>
                  {linha.categoryName}
                </Text>
                <Text variant="captionBody" tone="muted">
                  {linha.entryCount === 1 ? '1 lançamento' : `${linha.entryCount} lançamentos`}
                </Text>
              </View>
              <Text
                variant="bodyStrong"
                style={{ color: negativo ? theme.colors.danger : theme.colors.text }}
              >
                {negativo ? '−' : ''}
                {formatBRL(cents(linha.totalCents))}
              </Text>
            </View>
          ))}
        </View>
      </Card>
    </View>
  );
}

function LinhaDeTotal({
  rotulo,
  valorCents,
  cor,
  prefixo = '',
  forte = false,
}: {
  readonly rotulo: string;
  readonly valorCents: number;
  readonly cor?: string;
  readonly prefixo?: string;
  readonly forte?: boolean;
}) {
  const theme = useTheme();

  return (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' }}>
      {forte ? (
        <Text variant="bodyStrong">{rotulo}</Text>
      ) : (
        <Text variant="body" tone="muted">
          {rotulo}
        </Text>
      )}
      <Text variant={forte ? 'subheading' : 'bodyStrong'} style={{ color: cor ?? theme.colors.text }}>
        {prefixo}
        {formatBRL(cents(valorCents))}
      </Text>
    </View>
  );
}
