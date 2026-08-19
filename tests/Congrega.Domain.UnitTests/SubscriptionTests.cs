using Congrega.Domain.Billing;

namespace Congrega.Domain.UnitTests;

/// <summary>
/// A máquina de estados da assinatura.
/// </summary>
/// <remarks>
/// <para>
/// O agregado existe desde a Onda 3 e o <c>TODO.md</c> o marcava como
/// concluído, mas ele nunca teve teste próprio: <c>Subscription.Create</c>
/// aparecia apenas como preparo de cenário em <c>RetentionAlertTests</c>, e
/// nenhuma transição — permitida ou proibida — era exercitada.
/// </para>
/// <para>
/// A lacuna deixou de ser teórica ao expor <c>Cancel</c> por HTTP: é
/// exatamente esta tabela de transições que decide se o endpoint responde
/// <c>200</c> ou <c>409</c>, e o caso que ela recusa (<c>Grace</c>) é
/// alcançável pela tela, porque <c>FindCurrentByUserAsync</c> devolve
/// assinatura em carência.
/// </para>
/// <para>
/// A referência das transições é o diagrama da §6 de
/// <c>docs/03-arquitetura.md</c> — estes testes são o que impede código e
/// documento de divergirem em silêncio.
/// </para>
/// </remarks>
public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FimDoPeriodo = Agora.AddDays(30);

    private static Subscription Pessoal() => Subscription.Create(
        planId: 7,
        tenantId: null,
        userId: 42,
        source: SubscriptionSource.AbacatePay,
        periodStart: Agora,
        periodEnd: FimDoPeriodo);

    private static Subscription Ativa()
    {
        var assinatura = Pessoal();
        assinatura.Activate(Agora);
        return assinatura;
    }

    // -------------------------------------------------------------------------
    // Criação
    // -------------------------------------------------------------------------

    [Fact]
    public void Nasce_pendente()
    {
        // Pendente, não ativa: quem ativa é a confirmação do pagamento. Nascer
        // ativa daria acesso a quem só abriu o checkout e nunca pagou.
        Assert.Equal(SubscriptionStatus.Pending, Pessoal().Status);
    }

    [Fact]
    public void Recusa_assinatura_sem_dono_e_com_dois_donos()
    {
        // Espelha o CHECK ck_sub_owner do banco. Uma assinatura de tenant E
        // usuário ao mesmo tempo não teria titular definido para cobrança.
        Assert.Throws<ArgumentException>(() => Subscription.Create(
            7, tenantId: null, userId: null, SubscriptionSource.AbacatePay, Agora, FimDoPeriodo));

        Assert.Throws<ArgumentException>(() => Subscription.Create(
            7, tenantId: 1, userId: 42, SubscriptionSource.AbacatePay, Agora, FimDoPeriodo));
    }

    [Fact]
    public void Recusa_periodo_que_termina_antes_de_comecar()
    {
        Assert.Throws<ArgumentException>(() => Subscription.Create(
            7, null, 42, SubscriptionSource.AbacatePay, Agora, Agora.AddDays(-1)));
    }

    // -------------------------------------------------------------------------
    // Cancelamento — o caminho que o endpoint novo expõe
    // -------------------------------------------------------------------------

    [Fact]
    public void Cancelar_nao_encurta_o_periodo_ja_pago()
    {
        // A regra mais importante deste agregado para o produto: cancelar é
        // parar de renovar, não perder acesso. Mover CurrentPeriodEnd para
        // agora revogaria dias que a pessoa pagou — e é o que gera chargeback.
        var assinatura = Ativa();

        assinatura.Cancel(Agora.AddDays(5));

        Assert.Equal(SubscriptionStatus.Canceled, assinatura.Status);
        Assert.Equal(FimDoPeriodo, assinatura.CurrentPeriodEnd);
        Assert.True(assinatura.CancelAtPeriodEnd);
        Assert.Equal(Agora.AddDays(5), assinatura.CanceledAt);
    }

    [Fact]
    public void Cancelamento_imediato_encerra_o_periodo_na_hora()
    {
        // A outra metade do contrato: `immediate` existe para o caso
        // administrativo (fraude, pedido de exclusão), e aí sim o período fecha.
        var assinatura = Ativa();
        var quando = Agora.AddDays(5);

        assinatura.Cancel(quando, immediate: true);

        Assert.Equal(quando, assinatura.CurrentPeriodEnd);
        Assert.False(assinatura.CancelAtPeriodEnd);
    }

    [Fact]
    public void Cancelar_em_atraso_e_permitido()
    {
        // PastDue ainda tem renovação futura para cancelar.
        var assinatura = Ativa();
        assinatura.MarkPastDue();

        assinatura.Cancel(Agora);

        Assert.Equal(SubscriptionStatus.Canceled, assinatura.Status);
    }

    [Fact]
    public void Cancelar_em_carencia_e_recusado()
    {
        // O caso que o endpoint precisa tratar como 409 e não como 500:
        // `FindCurrentByUserAsync` devolve Grace, mas o diagrama da §6 só liga
        // Grace a Active e Expired. Em carência a cobrança JÁ falhou e não há
        // renovação a cancelar — a assinatura está encerrando sozinha.
        var assinatura = Ativa();
        assinatura.MarkPastDue();
        assinatura.EnterGrace(Agora.AddDays(3), Agora);

        var erro = Assert.Throws<InvalidSubscriptionTransitionException>(
            () => assinatura.Cancel(Agora));

        Assert.Equal(SubscriptionStatus.Grace, erro.From);
        Assert.Equal(SubscriptionStatus.Canceled, erro.To);
    }

    [Fact]
    public void Cancelar_assinatura_pendente_e_recusado()
    {
        // Pendente nunca foi paga: o caminho dela é expirar, não cancelar.
        var erro = Assert.Throws<InvalidSubscriptionTransitionException>(
            () => Pessoal().Cancel(Agora));

        Assert.Equal(SubscriptionStatus.Pending, erro.From);
    }

    [Fact]
    public void Cancelada_pode_ser_reativada_dentro_do_periodo()
    {
        // Quem cancelou e se arrependeu antes do fim do período pago volta sem
        // cobrança nova — o acesso nunca chegou a cair.
        var assinatura = Ativa();
        assinatura.Cancel(Agora);

        assinatura.Activate(Agora.AddDays(1));

        Assert.Equal(SubscriptionStatus.Active, assinatura.Status);
        Assert.Equal(FimDoPeriodo, assinatura.CurrentPeriodEnd);
    }

    // -------------------------------------------------------------------------
    // Demais transições da §6
    // -------------------------------------------------------------------------

    [Fact]
    public void Ativar_emite_evento_e_limpa_a_carencia()
    {
        var assinatura = Pessoal();

        assinatura.Activate(Agora);

        Assert.Equal(SubscriptionStatus.Active, assinatura.Status);
        Assert.Null(assinatura.GraceUntil);
        Assert.Contains(assinatura.DomainEvents, e => e is SubscriptionActivated);
    }

    [Fact]
    public void Renovar_estende_o_periodo_e_empurra_o_inicio()
    {
        var assinatura = Ativa();
        var novoFim = FimDoPeriodo.AddDays(30);

        assinatura.Renew(novoFim, Agora);

        Assert.Equal(FimDoPeriodo, assinatura.CurrentPeriodStart);
        Assert.Equal(novoFim, assinatura.CurrentPeriodEnd);
        Assert.Equal(SubscriptionStatus.Active, assinatura.Status);
    }

    [Fact]
    public void Renovar_para_tras_e_recusado()
    {
        // Webhook de renovação fora de ordem não pode encurtar o que já foi
        // pago — mesma defesa que `Entitlement.ExtendTo` faz do outro lado.
        var assinatura = Ativa();

        Assert.Throws<ArgumentException>(() => assinatura.Renew(FimDoPeriodo.AddDays(-1), Agora));
    }

    [Fact]
    public void Carencia_registra_o_prazo_e_emite_evento()
    {
        var assinatura = Ativa();
        assinatura.MarkPastDue();
        var ate = Agora.AddDays(3);

        assinatura.EnterGrace(ate, Agora);

        Assert.Equal(SubscriptionStatus.Grace, assinatura.Status);
        Assert.Equal(ate, assinatura.GraceUntil);
        Assert.Contains(assinatura.DomainEvents, e => e is SubscriptionEnteredGrace);
    }

    [Fact]
    public void Expirada_e_terminal()
    {
        // Nenhuma transição sai de Expired. É o que impede um webhook atrasado
        // de "reativar" uma assinatura encerrada meses atrás e conceder acesso.
        var assinatura = Ativa();
        assinatura.MarkPastDue();
        assinatura.EnterGrace(Agora.AddDays(3), Agora);
        assinatura.Expire(Agora.AddDays(4));

        Assert.Equal(SubscriptionStatus.Expired, assinatura.Status);
        Assert.Throws<InvalidSubscriptionTransitionException>(() => assinatura.Activate(Agora.AddDays(5)));
        Assert.Throws<InvalidSubscriptionTransitionException>(() => assinatura.Cancel(Agora.AddDays(5)));
        Assert.Throws<InvalidSubscriptionTransitionException>(() => assinatura.MarkPastDue());
    }

    [Fact]
    public void Retencao_alcanca_apenas_os_estados_em_que_renovar_faz_sentido()
    {
        // O motor de retenção manda e-mail de "sua assinatura vence"; mandá-lo
        // para quem já expirou ou cancelou é ruído que custa credibilidade.
        var ativa = Ativa();
        Assert.True(ativa.IsEligibleForRetentionAlerts());

        var cancelada = Ativa();
        cancelada.Cancel(Agora);
        Assert.False(cancelada.IsEligibleForRetentionAlerts());

        var pendente = Pessoal();
        Assert.False(pendente.IsEligibleForRetentionAlerts());
    }
}
