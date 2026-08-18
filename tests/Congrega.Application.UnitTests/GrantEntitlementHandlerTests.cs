using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

/// <summary>
/// Transforma pagamento confirmado em direito de acesso — a peça que faltava
/// para "pagamento confirmado vira acesso" ser verdade de ponta a ponta.
/// </summary>
/// <remarks>
/// Zero testes existiam para este handler antes desta rodada, apesar de já
/// estar escrito: compilava, tinha lógica correta a olho nu, mas nunca tinha
/// sido exercitado. Agora que está ligado ao Outbox de verdade
/// (<see cref="PaymentConfirmedOutboxHandler"/>/<see cref="PaymentRefundedOutboxHandler"/>),
/// cada decisão de negócio precisa de prova.
/// </remarks>
public sealed class GrantEntitlementHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private const long Usuario = 42;
    private const long Plano = 7;

    private static (GrantEntitlementHandler Handler, FakeEntitlementRepository Entitlements,
        FakeSubscriptionStore Subscriptions, FakeUnitOfWork UnitOfWork) Montar()
    {
        var entitlements = new FakeEntitlementRepository();
        var subscriptions = new FakeSubscriptionStore();
        var unitOfWork = new FakeUnitOfWork();

        var handler = new GrantEntitlementHandler(
            entitlements,
            subscriptions,
            unitOfWork,
            new FakeTimeProvider(Agora),
            NullLogger<GrantEntitlementHandler>.Instance);

        return (handler, entitlements, subscriptions, unitOfWork);
    }

    private static Subscription Assinatura(long id, long userId, long planId, DateTimeOffset periodEnd)
    {
        var assinatura = Subscription.Create(
            planId, tenantId: null, userId: userId, SubscriptionSource.AbacatePay, Agora.AddDays(-1), periodEnd);

        typeof(Subscription).GetProperty(nameof(Subscription.Id))!.SetValue(assinatura, id);
        return assinatura;
    }

    [Fact]
    public async Task Concede_direito_novo_quando_nao_existe_nenhum()
    {
        var (handler, entitlements, subscriptions, unitOfWork) = Montar();
        subscriptions.Registrar(Assinatura(id: 10, userId: Usuario, planId: Plano, periodEnd: Agora.AddDays(30)));

        await handler.GrantAsync(
            new PaymentConfirmed(PaymentId: 1, SubscriptionId: 10, UserId: Usuario, AmountCents: 2990, Agora),
            CancellationToken.None);

        var direito = Assert.Single(entitlements.Todos);
        Assert.Equal(Usuario, direito.UserId);
        Assert.Equal(Plano, direito.PlanId);
        Assert.Equal(Agora.AddDays(30), direito.ExpiresAt);
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task Renovacao_estende_o_direito_existente_em_vez_de_duplicar()
    {
        // Reprocessar o mesmo webhook (ou um segundo ciclo de cobrança) não pode
        // virar uma segunda linha de acesso para o mesmo plano.
        var (handler, entitlements, subscriptions, unitOfWork) = Montar();
        subscriptions.Registrar(Assinatura(id: 10, userId: Usuario, planId: Plano, periodEnd: Agora.AddDays(60)));
        entitlements.Add(Entitlement.GrantForPlan(
            Usuario, Plano, EntitlementSource.Subscription, Agora,
            expiresAt: Agora.AddDays(30), subscriptionId: 10, paymentId: 1));

        await handler.GrantAsync(
            new PaymentConfirmed(PaymentId: 2, SubscriptionId: 10, UserId: Usuario, AmountCents: 2990, Agora),
            CancellationToken.None);

        var direito = Assert.Single(entitlements.Todos);
        Assert.Equal(Agora.AddDays(60), direito.ExpiresAt);
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task Pagamento_de_igreja_nao_concede_entitlement()
    {
        // B2B: o que a igreja compra é o ChMS, cujo acesso vem da membership —
        // não desta tabela.
        var (handler, entitlements, _, unitOfWork) = Montar();

        await handler.GrantAsync(
            new PaymentConfirmed(PaymentId: 1, SubscriptionId: null, UserId: null, AmountCents: 9900, Agora),
            CancellationToken.None);

        Assert.Empty(entitlements.Todos);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task Pagamento_sem_assinatura_nao_concede_nada()
    {
        // Compra avulsa exigiria o pacote — ainda não implementado. Não deve
        // lançar, só não conceder.
        var (handler, entitlements, _, unitOfWork) = Montar();

        await handler.GrantAsync(
            new PaymentConfirmed(PaymentId: 1, SubscriptionId: null, UserId: Usuario, AmountCents: 2990, Agora),
            CancellationToken.None);

        Assert.Empty(entitlements.Todos);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task Assinatura_referenciada_mas_inexistente_nao_concede_nada()
    {
        var (handler, entitlements, _, unitOfWork) = Montar();

        await handler.GrantAsync(
            new PaymentConfirmed(PaymentId: 1, SubscriptionId: 999, UserId: Usuario, AmountCents: 2990, Agora),
            CancellationToken.None);

        Assert.Empty(entitlements.Todos);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task Estorno_revoga_os_direitos_do_pagamento()
    {
        var (handler, entitlements, _, unitOfWork) = Montar();
        var direito = Entitlement.GrantForPlan(
            Usuario, Plano, EntitlementSource.Subscription, Agora,
            expiresAt: Agora.AddDays(30), subscriptionId: 10, paymentId: 1);
        entitlements.Add(direito);

        await handler.RevokeAsync(new PaymentRefunded(PaymentId: 1, SubscriptionId: 10, Agora), CancellationToken.None);

        Assert.NotNull(direito.RevokedAt);
        Assert.Equal(RevocationReason.Refund, direito.RevokedReason);
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task Estorno_processado_duas_vezes_nao_comita_na_segunda()
    {
        // Idempotência do lado do handler: reentrega do Outbox não pode gerar
        // um segundo commit vazio nem sobrescrever o motivo da revogação.
        var (handler, entitlements, _, unitOfWork) = Montar();
        entitlements.Add(Entitlement.GrantForPlan(
            Usuario, Plano, EntitlementSource.Subscription, Agora,
            expiresAt: Agora.AddDays(30), subscriptionId: 10, paymentId: 1));

        await handler.RevokeAsync(new PaymentRefunded(1, 10, Agora), CancellationToken.None);
        await handler.RevokeAsync(new PaymentRefunded(1, 10, Agora), CancellationToken.None);

        Assert.Equal(1, unitOfWork.Commits);
    }

    // -------------------------------------------------------------------------
    // Dublês
    // -------------------------------------------------------------------------

    private sealed class FakeEntitlementRepository : IEntitlementRepository
    {
        public List<Entitlement> Todos { get; } = [];

        public Task<IReadOnlyList<Entitlement>> ListActiveAsync(
            long userId, DateTimeOffset moment, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Entitlement?> FindActiveForPlanAsync(
            long userId, long planId, DateTimeOffset moment, CancellationToken cancellationToken) =>
            Task.FromResult(Todos.FirstOrDefault(
                e => e.UserId == userId && e.PlanId == planId && e.IsActiveOn(moment)));

        public Task<IReadOnlyList<Entitlement>> ListBySubscriptionAsync(
            long subscriptionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Entitlement>> ListByPaymentAsync(
            long paymentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Entitlement>>(
                Todos.Where(e => e.SourcePaymentId == paymentId).ToList());

        public void Add(Entitlement entitlement) => Todos.Add(entitlement);
    }

    private sealed class FakeSubscriptionStore : ISubscriptionStore
    {
        private readonly Dictionary<long, Subscription> _porId = [];

        public void Registrar(Subscription assinatura) => _porId[assinatura.Id] = assinatura;

        public Task<Subscription?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(_porId.GetValueOrDefault(id));

        public Task<Subscription?> FindActiveByUserAsync(long userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Subscription?> FindReusableForCheckoutAsync(
            long userId, long planId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(Subscription subscription) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Commits++;
            return Task.FromResult(1);
        }
    }
}
