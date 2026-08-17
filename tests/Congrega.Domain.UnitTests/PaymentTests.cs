using Congrega.Domain.Billing;

namespace Congrega.Domain.UnitTests;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static Payment Cobranca() => Payment.Start(
        amountCents: 2990,
        idempotencyKey: "chk_abc",
        source: SubscriptionSource.AbacatePay,
        now: Agora,
        userId: 1,
        subscriptionId: 10);

    [Fact]
    public void Start_nasce_pendente()
    {
        Assert.Equal(PaymentStatus.Pending, Cobranca().Status);
    }

    [Fact]
    public void Start_recusa_valor_zero_ou_negativo()
    {
        Assert.Throws<ArgumentException>(() => Payment.Start(
            0, "k", SubscriptionSource.AbacatePay, Agora, userId: 1));
        Assert.Throws<ArgumentException>(() => Payment.Start(
            -100, "k", SubscriptionSource.AbacatePay, Agora, userId: 1));
    }

    [Fact]
    public void Start_recusa_pagamento_sem_titular()
    {
        // A policy de RLS de `payments` filtra por tenant OU user. Sem os dois,
        // a linha ficaria invisível para todo mundo.
        Assert.Throws<ArgumentException>(() => Payment.Start(
            100, "k", SubscriptionSource.AbacatePay, Agora, userId: null, tenantId: null));
    }

    [Fact]
    public void Start_recusa_chave_de_idempotencia_vazia()
    {
        Assert.Throws<ArgumentException>(() => Payment.Start(
            100, "  ", SubscriptionSource.AbacatePay, Agora, userId: 1));
    }

    [Fact]
    public void Confirm_muda_para_pago_e_emite_evento()
    {
        var pagamento = Cobranca();

        bool mudou = pagamento.Confirm(Agora, Agora);

        Assert.True(mudou);
        Assert.Equal(PaymentStatus.Paid, pagamento.Status);
        Assert.Single(pagamento.DomainEvents);
        Assert.IsType<PaymentConfirmed>(pagamento.DomainEvents.Single());
    }

    [Fact]
    public void Confirm_repetido_e_idempotente_e_NAO_emite_segundo_evento()
    {
        // O caso que a skill de segurança descreve: "Webhook A, Webhook A
        // duplicado, Webhook A duplicado novamente" precisa resultar em
        // 1 evento processado e 0 acessos duplicados. Um segundo evento aqui
        // viraria uma segunda concessão de entitlement lá na frente.
        var pagamento = Cobranca();

        Assert.True(pagamento.Confirm(Agora, Agora));
        Assert.False(pagamento.Confirm(Agora, Agora));
        Assert.False(pagamento.Confirm(Agora, Agora));

        Assert.Single(pagamento.DomainEvents);
    }

    [Fact]
    public void Confirm_recusa_reabrir_pagamento_estornado()
    {
        // Webhook atrasado de "pago" chegando depois do estorno não pode
        // ressuscitar a cobrança — e o acesso que ela concedia.
        var pagamento = Cobranca();
        pagamento.Confirm(Agora, Agora);
        pagamento.Refund(Agora);

        Assert.Throws<InvalidOperationException>(() => pagamento.Confirm(Agora, Agora));
    }

    [Fact]
    public void Fail_depois_de_pago_e_ignorado()
    {
        // Evento fora de ordem, não informação nova.
        var pagamento = Cobranca();
        pagamento.Confirm(Agora, Agora);

        Assert.False(pagamento.Fail("qualquer", Agora));
        Assert.Equal(PaymentStatus.Paid, pagamento.Status);
    }

    [Fact]
    public void Fail_repetido_e_idempotente()
    {
        var pagamento = Cobranca();

        Assert.True(pagamento.Fail("saldo insuficiente", Agora));
        Assert.False(pagamento.Fail("saldo insuficiente", Agora));
        Assert.Equal(PaymentStatus.Failed, pagamento.Status);
    }

    [Fact]
    public void Refund_exige_pagamento_pago()
    {
        var pagamento = Cobranca();
        Assert.Throws<InvalidOperationException>(() => pagamento.Refund(Agora));
    }

    [Fact]
    public void Refund_repetido_e_idempotente_e_emite_um_evento()
    {
        var pagamento = Cobranca();
        pagamento.Confirm(Agora, Agora);
        pagamento.ClearDomainEvents();

        Assert.True(pagamento.Refund(Agora));
        Assert.False(pagamento.Refund(Agora));

        Assert.Single(pagamento.DomainEvents);
        Assert.IsType<PaymentRefunded>(pagamento.DomainEvents.Single());
    }

    [Fact]
    public void Chargeback_e_estado_proprio()
    {
        var pagamento = Cobranca();
        pagamento.Confirm(Agora, Agora);
        pagamento.Refund(Agora, chargeback: true);

        Assert.Equal(PaymentStatus.Chargeback, pagamento.Status);
    }

    [Fact]
    public void AttachGatewayCharge_recusa_trocar_por_outra_cobranca()
    {
        // Repontar para outra cobrança faria a conciliação comparar coisas
        // diferentes e o fetch-on-notify consultar o objeto errado.
        var pagamento = Cobranca();
        pagamento.AttachGatewayCharge("ch_1", Agora);

        Assert.Throws<InvalidOperationException>(
            () => pagamento.AttachGatewayCharge("ch_2", Agora));
    }

    [Fact]
    public void AttachGatewayCharge_com_o_mesmo_id_e_idempotente()
    {
        var pagamento = Cobranca();
        pagamento.AttachGatewayCharge("ch_1", Agora);
        pagamento.AttachGatewayCharge("ch_1", Agora);

        Assert.Equal("ch_1", pagamento.GatewayChargeId);
    }
}

public sealed class EntitlementTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static Entitlement Direito(DateTimeOffset? expira = null) =>
        Entitlement.GrantForPlan(
            userId: 1,
            planId: 1,
            source: EntitlementSource.Subscription,
            now: Agora,
            expiresAt: expira ?? Agora.AddDays(30));

    [Fact]
    public void Direito_novo_esta_ativo()
    {
        Assert.True(Direito().IsActiveOn(Agora));
    }

    [Fact]
    public void Direito_vencido_nao_esta_ativo()
    {
        var direito = Direito(Agora.AddDays(1));
        Assert.False(direito.IsActiveOn(Agora.AddDays(2)));
    }

    [Fact]
    public void Direito_revogado_nao_esta_ativo_MESMO_dentro_do_prazo()
    {
        // O erro clássico: checar só a validade deixa um estornado assistindo
        // até a data de expiração original.
        var direito = Direito(Agora.AddDays(30));
        direito.Revoke(RevocationReason.Refund, Agora);

        Assert.False(direito.IsActiveOn(Agora.AddDays(1)));
    }

    [Fact]
    public void Compra_avulsa_nao_vence()
    {
        var direito = Entitlement.GrantForPack(
            userId: 1, resourcePackId: 5, source: EntitlementSource.OneOffPurchase, now: Agora);

        Assert.Null(direito.ExpiresAt);
        Assert.True(direito.IsActiveOn(Agora.AddYears(10)));
    }

    [Fact]
    public void GrantForPlan_recusa_nascer_vencido()
    {
        Assert.Throws<ArgumentException>(() => Entitlement.GrantForPlan(
            userId: 1, planId: 1, source: EntitlementSource.Subscription,
            now: Agora, expiresAt: Agora.AddDays(-1)));
    }

    [Fact]
    public void ExtendTo_estende_o_prazo()
    {
        var direito = Direito(Agora.AddDays(30));
        direito.ExtendTo(Agora.AddDays(60));

        Assert.Equal(Agora.AddDays(60), direito.ExpiresAt);
    }

    [Fact]
    public void ExtendTo_NUNCA_encurta()
    {
        // Webhook de renovação fora de ordem, chegando depois de outro mais
        // novo, tiraria dias já pagos do assinante.
        var direito = Direito(Agora.AddDays(60));
        direito.ExtendTo(Agora.AddDays(30));

        Assert.Equal(Agora.AddDays(60), direito.ExpiresAt);
    }

    [Fact]
    public void Revoke_e_idempotente_e_preserva_o_primeiro_motivo()
    {
        var direito = Direito();

        Assert.True(direito.Revoke(RevocationReason.Refund, Agora));
        Assert.False(direito.Revoke(RevocationReason.Admin, Agora.AddDays(1)));

        Assert.Equal(RevocationReason.Refund, direito.RevokedReason);
        Assert.Equal(Agora, direito.RevokedAt);
    }

    [Fact]
    public void Revogar_nao_apaga_o_registro()
    {
        // O ADR-015 exige que o histórico sobreviva: apagar faria estorno e
        // cancelamento ficarem indistinguíveis na auditoria.
        var direito = Direito();
        direito.Revoke(RevocationReason.Chargeback, Agora);

        Assert.NotNull(direito.RevokedAt);
        Assert.Equal(RevocationReason.Chargeback, direito.RevokedReason);
        Assert.Equal(1, direito.UserId);
        Assert.Equal(1, direito.PlanId);
    }
}
