import type {
  CheckoutResult,
  Payment,
  PaymentState,
  Plan,
  SubscriptionState,
  SubscriptionStatus,
} from '@congrega/api-client/billing';
import { describeError } from '@congrega/api-client/errors';
import { describeRenewal, formatDate } from '@congrega/core/datetime';
import { cents, formatBRL } from '@congrega/core/money';
import { AsyncContent } from '@congrega/ui/AsyncContent';
import { Button } from '@congrega/ui/Button';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { useState } from 'react';
import { Alert, Platform, ScrollView, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useBilling } from '../../../src/useBilling';

const ESTADO_LABEL: Record<SubscriptionState, string> = {
  Pending: 'Pendente',
  Active: 'Ativa',
  PastDue: 'Pagamento atrasado',
  Grace: 'Em carência',
  Canceled: 'Cancelada',
  Expired: 'Expirada',
};

const PAGAMENTO_LABEL: Record<PaymentState, string> = {
  Pending: 'Aguardando',
  Paid: 'Pago',
  Failed: 'Recusado',
  Refunded: 'Estornado',
  Chargeback: 'Contestado',
};

const PERIODO_LABEL: Record<Plan['billingPeriod'], string> = {
  1: '/mês',
  2: '/ano',
};

/**
 * Estados em que cancelar significa alguma coisa.
 *
 * Espelha a tabela de transições do agregado (`docs/03-arquitetura.md` §6):
 * `Grace` **não** entra, porque a cobrança já falhou e a assinatura está
 * encerrando sozinha — não há renovação futura para cancelar, e o servidor
 * responde 409. Oferecer o botão ali seria oferecer uma porta que não abre.
 */
const CANCELAVEL: readonly SubscriptionState[] = ['Active', 'PastDue'];

export default function Assinatura() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { assinatura, planos, pagamentos, carregando, erro, recarregar, assinar, cancelar } =
    useBilling();

  const [selecionando, setSelecionando] = useState<string | null>(null);
  const [erroCheckout, setErroCheckout] = useState<string | null>(null);
  const [resultado, setResultado] = useState<CheckoutResult | null>(null);
  const [cancelando, setCancelando] = useState(false);
  const [erroCancelamento, setErroCancelamento] = useState<string | null>(null);

  async function escolher(planCode: string) {
    setSelecionando(planCode);
    setErroCheckout(null);

    try {
      setResultado(await assinar(planCode));
    } catch (causa) {
      setErroCheckout(describeError(causa));
    } finally {
      setSelecionando(null);
    }
  }

  async function confirmarCancelamento() {
    setCancelando(true);
    setErroCancelamento(null);

    try {
      await cancelar();
    } catch (causa) {
      setErroCancelamento(describeError(causa));
    } finally {
      setCancelando(false);
    }
  }

  function pedirCancelamento() {
    // O texto diz o que de fato acontece: para de renovar, o acesso fica. Um
    // "tem certeza?" seco faria o usuário supor que perde o que já pagou — e a
    // desistência por medo é tão ruim quanto o cancelamento por engano.
    const titulo = 'Cancelar assinatura';
    const detalhe =
      'A renovação automática para. Seu acesso continua até o fim do período já pago.';

    if (Platform.OS === 'web') {
      // eslint-disable-next-line no-alert
      if (globalThis.confirm(`${titulo}\n\n${detalhe}`)) {
        void confirmarCancelamento();
      }
      return;
    }

    Alert.alert(titulo, detalhe, [
      { text: 'Manter assinatura', style: 'cancel' },
      { text: 'Cancelar assinatura', style: 'destructive', onPress: () => void confirmarCancelamento() },
    ]);
  }

  return (
    <Screen padded={false} wide>
      <ScrollView
        contentContainerStyle={{
          paddingTop: insets.top + theme.space[16],
          paddingHorizontal: theme.space[24],
          paddingBottom: insets.bottom + theme.space[32],
          gap: theme.space[16],
          maxWidth: theme.layout.pageMaxWidth,
          width: '100%',
          alignSelf: 'center',
        }}
      >
        <View style={{ gap: theme.space[4] }}>
          <Text variant="eyebrow" tone="muted">
            CONGREGA+
          </Text>
          <Text variant="heading">Sua assinatura</Text>
        </View>

        <AsyncContent
          loading={carregando}
          failure={erro}
          errorTitle="Não deu para carregar sua assinatura"
          onRetry={recarregar}
        >
          <>
            {assinatura?.hasSubscription === true ? (
              <StatusDaAssinatura
                assinatura={assinatura}
                cancelando={cancelando}
                erroCancelamento={erroCancelamento}
                onCancelar={pedirCancelamento}
              />
            ) : (
              <VitrineDePlanos
                planos={planos}
                selecionando={selecionando}
                resultado={resultado}
                erroCheckout={erroCheckout}
                onEscolher={escolher}
              />
            )}

            {/* Fora do condicional de propósito: quem cancelou e voltou a
                "sem assinatura" continua com direito de ver o que pagou. */}
            <HistoricoDePagamentos pagamentos={pagamentos} />
          </>
        </AsyncContent>
      </ScrollView>
    </Screen>
  );
}

function StatusDaAssinatura({
  assinatura,
  cancelando,
  erroCancelamento,
  onCancelar,
}: {
  readonly assinatura: SubscriptionStatus;
  readonly cancelando: boolean;
  readonly erroCancelamento: string | null;
  readonly onCancelar: () => void;
}) {
  const theme = useTheme();
  const estado: SubscriptionState | null = assinatura.status;

  // Já cancelada não oferece cancelar de novo: a chamada seria aceita pelo
  // domínio (Canceled → Canceled não está na tabela, então daria 409), mas o
  // problema real é oferecer uma ação que não muda nada.
  const podeCancelar =
    estado !== null && CANCELAVEL.includes(estado) && !assinatura.cancelAtPeriodEnd;

  return (
    <Card>
      <View style={{ gap: theme.space[12] }}>
        <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' }}>
          <Text variant="subheading">{assinatura.planName ?? 'Plano Congrega+'}</Text>
          {estado !== null && <EyebrowPill label={ESTADO_LABEL[estado]} tone="badge" />}
        </View>

        {assinatura.cancelAtPeriodEnd && (
          <Text variant="body" tone="muted">
            Cancelada — o acesso continua até o fim do período já pago.
          </Text>
        )}

        {assinatura.currentPeriodEnd !== null && (
          <Text variant="body" tone="muted">
            {describeRenewal(assinatura.currentPeriodEnd)}
          </Text>
        )}

        {estado === 'Grace' && assinatura.graceUntil !== null && (
          <Text variant="captionBody" style={{ color: theme.colors.danger }}>
            Pagamento não confirmado — o acesso continua até{' '}
            {describeRenewal(assinatura.graceUntil).toLowerCase()}. Verifique sua forma de pagamento.
          </Text>
        )}

        {podeCancelar && (
          <Button
            label="Cancelar assinatura"
            variant="outline"
            loading={cancelando}
            onPress={onCancelar}
          />
        )}

        {erroCancelamento !== null && (
          <Text variant="captionBody" style={{ color: theme.colors.danger }}>
            {erroCancelamento}
          </Text>
        )}
      </View>
    </Card>
  );
}

function HistoricoDePagamentos({ pagamentos }: { readonly pagamentos: readonly Payment[] }) {
  const theme = useTheme();

  // Sem cobrança nenhuma, um cabeçalho "Pagamentos" seguido de vazio só ocupa
  // espaço — quem nunca assinou não tem histórico a explicar.
  if (pagamentos.length === 0) {
    return null;
  }

  return (
    <View style={{ gap: theme.space[8] }}>
      <Text variant="eyebrow" tone="muted">
        PAGAMENTOS
      </Text>
      <Card>
        <View style={{ gap: theme.space[12] }}>
          {pagamentos.map((pagamento) => (
            <View
              key={pagamento.id}
              style={{ flexDirection: 'row', alignItems: 'center', gap: theme.space[12] }}
            >
              <View style={{ flex: 1, gap: 2 }}>
                <Text variant="body">{formatBRL(cents(pagamento.amountCents))}</Text>
                <Text variant="captionBody" tone="muted">
                  {/* A data que importa é a do pagamento quando ele ocorreu; a
                      da criação é o que existe enquanto a cobrança está aberta. */}
                  {formatDate(pagamento.paidAt ?? pagamento.createdAt)}
                  {pagamento.method !== null ? ` · ${pagamento.method}` : ''}
                </Text>
              </View>
              <Text
                variant="captionBody"
                style={{
                  color:
                    pagamento.status === 'Paid' ? theme.colors.text
                    : pagamento.status === 'Pending' ? theme.colors.textMuted
                    : theme.colors.danger,
                }}
              >
                {PAGAMENTO_LABEL[pagamento.status]}
              </Text>
            </View>
          ))}
        </View>
      </Card>
    </View>
  );
}

function VitrineDePlanos({
  planos,
  selecionando,
  resultado,
  erroCheckout,
  onEscolher,
}: {
  readonly planos: readonly Plan[];
  readonly selecionando: string | null;
  readonly resultado: CheckoutResult | null;
  readonly erroCheckout: string | null;
  readonly onEscolher: (planCode: string) => void;
}) {
  const theme = useTheme();

  if (resultado !== null) {
    return (
      <Card>
        <View style={{ gap: theme.space[12] }}>
          <Text variant="subheading">Cobrança criada</Text>
          <Text variant="body" tone="muted">
            {formatBRL(cents(resultado.amountCents))}
            {resultado.planName !== null ? ` · ${resultado.planName}` : ''} — assim que o
            pagamento for confirmado, sua assinatura passa a valer automaticamente.
          </Text>
          {resultado.pixCode !== null && (
            <View style={{ gap: theme.space[4] }}>
              <Text variant="captionBody" tone="muted">
                CÓDIGO PIX COPIA E COLA
              </Text>
              <Text variant="body" selectable>
                {resultado.pixCode}
              </Text>
            </View>
          )}
        </View>
      </Card>
    );
  }

  if (planos.length === 0) {
    return (
      <EmptyState
        title="Nenhum plano disponível"
        description="Não há plano Congrega+ ativo no momento. Volte mais tarde."
      />
    );
  }

  return (
    <View style={{ gap: theme.space[12] }}>
      <Text variant="body" tone="muted">
        Assine para ter acesso ao conteúdo Congrega+, independente da sua igreja ter o Congrega
        Church ou não.
      </Text>

      {planos.map((plano) => (
        <Card key={plano.code}>
          <View style={{ gap: theme.space[12] }}>
            <View style={{ flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' }}>
              <Text variant="bodyStrong">{plano.name}</Text>
              <Text variant="bodyStrong">
                {formatBRL(cents(plano.priceCents))}
                <Text variant="captionBody" tone="muted">
                  {PERIODO_LABEL[plano.billingPeriod]}
                </Text>
              </Text>
            </View>
            <SignatureButton
              label="Assinar"
              loading={selecionando === plano.code}
              onPress={() => onEscolher(plano.code)}
            />
          </View>
        </Card>
      ))}

      {erroCheckout !== null && (
        <Text variant="captionBody" style={{ color: theme.colors.danger }}>
          {erroCheckout}
        </Text>
      )}
    </View>
  );
}
