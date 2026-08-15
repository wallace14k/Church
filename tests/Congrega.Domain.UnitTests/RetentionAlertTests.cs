using Congrega.Domain.Billing;
using Congrega.Domain.Retention;

namespace Congrega.Domain.UnitTests;

/// <summary>
/// Testes da chave de deduplicação.
/// </summary>
/// <remarks>
/// Esta chave é a garantia de correção do motor de retenção: é a constraint
/// <c>UNIQUE (dedupe_key)</c> que impede alerta duplicado, e não o lock distribuído.
/// Um defeito aqui não quebra teste nenhum em produção — ele apenas faz usuários
/// receberem e-mail repetido, ou pior, deixarem de receber. Daí a cobertura.
/// </remarks>
public sealed class RetentionAlertTests
{
    private static RetentionAlert Alert(
        long subscriptionId = 9182,
        long userId = 1337,
        RetentionAlertWindow window = RetentionAlertWindow.D7,
        NotificationChannel channel = NotificationChannel.Email) => new()
        {
            SubscriptionId = subscriptionId,
            UserId = userId,
            TenantId = null,
            PeriodEnd = new DateOnly(2026, 9, 1),
            Window = window,
            Channel = channel,
            TemplateCode = "retention.d7",
            PayloadJson = "{}"
        };

    [Fact]
    public void Chave_tem_formato_estavel()
    {
        Assert.Equal("retention:9182:1337:2026-09-01:D7:Email", Alert().DedupeKey);
    }

    [Fact]
    public void Canais_diferentes_produzem_chaves_diferentes()
    {
        // Sem o canal na chave, e-mail, push e banner colidiriam e apenas o primeiro
        // seria entregue — o usuário perderia o push justamente na janela mais urgente.
        var email = Alert(channel: NotificationChannel.Email).DedupeKey;
        var push = Alert(channel: NotificationChannel.Push).DedupeKey;
        var banner = Alert(channel: NotificationChannel.InAppBanner).DedupeKey;

        Assert.Equal(3, new HashSet<string> { email, push, banner }.Count);
    }

    [Fact]
    public void Usuarios_diferentes_na_mesma_assinatura_produzem_chaves_diferentes()
    {
        // Caso B2B: uma assinatura de igreja tem vários administradores. Todos
        // precisam ser avisados do vencimento — este é o produto que gera a receita
        // recorrente do ChMS, e um único admin avisado seria falha silenciosa.
        var admin1 = Alert(userId: 100).DedupeKey;
        var admin2 = Alert(userId: 200).DedupeKey;

        Assert.NotEqual(admin1, admin2);
    }

    [Fact]
    public void Ciclos_de_cobranca_diferentes_produzem_chaves_diferentes()
    {
        // A propriedade que faz o motor continuar funcionando no segundo mês:
        // renovada a assinatura, muda o period_end e a chave se renova sozinha.
        // Sem isso, o usuário receberia alerta uma única vez na vida.
        var cycle1 = Alert() with { PeriodEnd = new DateOnly(2026, 9, 1) };
        var cycle2 = Alert() with { PeriodEnd = new DateOnly(2026, 10, 1) };

        Assert.NotEqual(cycle1.DedupeKey, cycle2.DedupeKey);
    }

    [Fact]
    public void Mesma_janela_no_mesmo_ciclo_produz_chave_identica()
    {
        // A propriedade que garante a deduplicação: dois ciclos do worker no mesmo
        // dia, ou duas réplicas simultâneas, geram exatamente a mesma chave — e o
        // banco descarta a segunda.
        Assert.Equal(Alert().DedupeKey, Alert().DedupeKey);
    }
}

/// <summary>Testes da máquina de estados da assinatura.</summary>
public sealed class SubscriptionStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static Subscription NewPersonalSubscription() =>
        Subscription.Create(
            planId: 1,
            tenantId: null,
            userId: 1337,
            source: SubscriptionSource.AbacatePay,
            periodStart: Now,
            periodEnd: Now.AddMonths(1));

    [Fact]
    public void Assinatura_nasce_pendente()
    {
        Assert.Equal(SubscriptionStatus.Pending, NewPersonalSubscription().Status);
    }

    [Fact]
    public void Nao_aceita_pertencer_a_tenant_e_usuario_ao_mesmo_tempo()
    {
        var ex = Assert.Throws<ArgumentException>(() => Subscription.Create(
            planId: 1, tenantId: 42, userId: 1337,
            source: SubscriptionSource.AbacatePay,
            periodStart: Now, periodEnd: Now.AddMonths(1)));

        Assert.Contains("nunca a ambos", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Webhook_atrasado_nao_reativa_assinatura_expirada()
    {
        // O cenário que a máquina de estados existe para impedir: provedores
        // reentregam e reordenam eventos. Um "pagamento confirmado" que chega
        // depois da expiração não pode conceder acesso.
        var subscription = NewPersonalSubscription();
        subscription.Activate(Now);
        subscription.MarkPastDue();
        subscription.EnterGrace(Now.AddDays(7), Now);
        subscription.Expire(Now.AddDays(8));

        var ex = Assert.Throws<InvalidSubscriptionTransitionException>(
            () => subscription.Activate(Now.AddDays(9)));

        Assert.Equal(SubscriptionStatus.Expired, ex.From);
        Assert.Equal(SubscriptionStatus.Active, ex.To);
    }

    [Fact]
    public void Cancelamento_nao_encerra_o_periodo_pago()
    {
        // Cancelou dia 15, pagou até dia 30: o acesso vai até o dia 30.
        // Confundir "cancelou" com "perdeu acesso" gera reclamação e chargeback.
        var subscription = NewPersonalSubscription();
        var periodEnd = subscription.CurrentPeriodEnd;
        subscription.Activate(Now);

        subscription.Cancel(Now.AddDays(15));

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.Equal(periodEnd, subscription.CurrentPeriodEnd);
        Assert.True(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void Estados_elegiveis_a_alerta_de_retencao_sao_apenas_aqueles_em_que_renovar_faz_sentido()
    {
        var subscription = NewPersonalSubscription();
        Assert.False(subscription.IsEligibleForRetentionAlerts()); // Pending

        subscription.Activate(Now);
        Assert.True(subscription.IsEligibleForRetentionAlerts());  // Active

        subscription.MarkPastDue();
        Assert.True(subscription.IsEligibleForRetentionAlerts());  // PastDue

        subscription.EnterGrace(Now.AddDays(7), Now);
        Assert.True(subscription.IsEligibleForRetentionAlerts());  // Grace

        subscription.Expire(Now.AddDays(8));
        Assert.False(subscription.IsEligibleForRetentionAlerts()); // Expired
    }
}
