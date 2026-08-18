import type {
  CheckoutResult,
  Plan,
  SubscriptionState,
  SubscriptionStatus,
} from '@congrega/api-client/billing';
import { describeError } from '@congrega/api-client/errors';
import { describeRenewal } from '@congrega/core/datetime';
import { cents, formatBRL } from '@congrega/core/money';
import { Card } from '@congrega/ui/Card';
import { EmptyState } from '@congrega/ui/EmptyState';
import { EyebrowPill } from '@congrega/ui/EyebrowPill';
import { Screen } from '@congrega/ui/Screen';
import { SignatureButton } from '@congrega/ui/SignatureButton';
import { Text } from '@congrega/ui/Text';
import { useTheme } from '@congrega/ui/theme';
import { useState } from 'react';
import { ActivityIndicator, ScrollView, View } from 'react-native';
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

const PERIODO_LABEL: Record<Plan['billingPeriod'], string> = {
  1: '/mês',
  2: '/ano',
};

export default function Assinatura() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { assinatura, planos, carregando, erro, recarregar, assinar } = useBilling();

  const [selecionando, setSelecionando] = useState<string | null>(null);
  const [erroCheckout, setErroCheckout] = useState<string | null>(null);
  const [resultado, setResultado] = useState<CheckoutResult | null>(null);

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

        {carregando ? (
          <View style={{ paddingVertical: theme.space[32], alignItems: 'center' }}>
            <ActivityIndicator color={theme.colors.text} />
          </View>
        ) : erro !== null ? (
          <EmptyState
            title="Não deu para carregar sua assinatura"
            description={erro}
            action={<SignatureButton label="Tentar de novo" onPress={recarregar} />}
          />
        ) : assinatura?.hasSubscription === true ? (
          <StatusDaAssinatura assinatura={assinatura} />
        ) : (
          <VitrineDePlanos
            planos={planos}
            selecionando={selecionando}
            resultado={resultado}
            erroCheckout={erroCheckout}
            onEscolher={escolher}
          />
        )}
      </ScrollView>
    </Screen>
  );
}

function StatusDaAssinatura({ assinatura }: { readonly assinatura: SubscriptionStatus }) {
  const theme = useTheme();
  const estado: SubscriptionState | null = assinatura.status;

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
      </View>
    </Card>
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
