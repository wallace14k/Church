using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Domain.Billing;
using Congrega.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

/// <summary>
/// Checkout do Congrega+.
/// </summary>
/// <remarks>
/// Cada teste aqui corresponde a uma decisão de segurança do handler. Removê-la
/// não quebra o caminho feliz — é justamente por isso que precisa de teste.
/// </remarks>
public sealed class StartCheckoutHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private const long Titular = 42;
    private const long Intruso = 99;

    private static PlanSnapshot PlanoB2C => new()
    {
        Id = 7,
        Code = "premium_monthly",
        Name = "Congrega+ Mensal",
        PriceCents = 2990,
        BillingPeriod = 1,
        Audience = PlanAudience.User,
    };

    private static PlanSnapshot PlanoB2B => new()
    {
        Id = 1,
        Code = "chms_basic",
        Name = "Congrega Church — Essencial",
        PriceCents = 9900,
        BillingPeriod = 1,
        Audience = PlanAudience.Tenant,
    };

    private static (StartCheckoutHandler Handler, FakePaymentRepository Payments, FakeGateway Gateway)
        Montar(params PlanSnapshot[] planos)
    {
        var payments = new FakePaymentRepository();
        var gateway = new FakeGateway();

        var handler = new StartCheckoutHandler(
            new FakePlanRepository(planos),
            payments,
            new FakeSubscriptionStore(),
            gateway,
            new FakeUnitOfWork(payments),
            new FakeTimeProvider(Agora),
            NullLogger<StartCheckoutHandler>.Instance);

        return (handler, payments, gateway);
    }

    private static StartCheckoutCommand Comando(
        string chave = "tentativa-1",
        long userId = Titular,
        string plano = "premium_monthly") => new()
        {
            UserId = userId,
            PlanCode = plano,
            IdempotencyKey = chave,
        };

    [Fact]
    public async Task Abre_cobranca_com_o_preco_do_banco()
    {
        var (handler, _, gateway) = Montar(PlanoB2C);

        var resultado = await handler.HandleAsync(Comando(), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Created, resultado.Outcome);
        Assert.Equal(2990, resultado.AmountCents);
        // O gateway é cobrado pelo valor do plano, não por nada vindo do cliente.
        Assert.Equal(2990, gateway.UltimaCobranca!.AmountCents);
    }

    [Fact]
    public async Task A_mesma_chave_devolve_a_MESMA_cobranca()
    {
        // O caso que a idempotência existe para cobrir: duplo clique, retry de
        // rede. Duas cobranças aqui seriam duas cobranças no cartão do usuário.
        var (handler, payments, gateway) = Montar(PlanoB2C);

        var primeira = await handler.HandleAsync(Comando(), CancellationToken.None);
        var segunda = await handler.HandleAsync(Comando(), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Created, primeira.Outcome);
        Assert.Equal(CheckoutOutcome.Reused, segunda.Outcome);
        Assert.Equal(primeira.PaymentId, segunda.PaymentId);
        Assert.Single(payments.Todos);
        Assert.Equal(1, gateway.Chamadas);
    }

    [Fact]
    public async Task Chaves_iguais_de_titulares_diferentes_nao_colidem()
    {
        // A constraint `uq_pay_idempotency_key` é UNIQUE sobre a tabela inteira.
        // Sem o prefixo do titular, o segundo usuário receberia de volta a
        // cobrança do primeiro — com o public_id dela. Vazamento financeiro.
        var (handler, payments, _) = Montar(PlanoB2C);

        var doTitular = await handler.HandleAsync(
            Comando(chave: "1", userId: Titular), CancellationToken.None);
        var doIntruso = await handler.HandleAsync(
            Comando(chave: "1", userId: Intruso), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Created, doTitular.Outcome);
        Assert.Equal(CheckoutOutcome.Created, doIntruso.Outcome);
        Assert.NotEqual(doTitular.PaymentId, doIntruso.PaymentId);
        Assert.Equal(2, payments.Todos.Count);
    }

    [Fact]
    public async Task Recusa_plano_de_igreja_comprado_como_pessoa()
    {
        // Sem a checagem de audiência, bastaria o código do plano para uma
        // pessoa física abrir cobrança do produto B2B.
        var (handler, payments, gateway) = Montar(PlanoB2B);

        var resultado = await handler.HandleAsync(
            Comando(plano: "chms_basic"), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.PlanUnavailable, resultado.Outcome);
        Assert.Empty(payments.Todos);
        Assert.Equal(0, gateway.Chamadas);
    }

    [Fact]
    public async Task Plano_inexistente_e_plano_de_audiencia_errada_respondem_igual()
    {
        // A resposta não pode distinguir os dois casos: distinguir entregaria a
        // quem sonda a lista de códigos de plano que existem.
        var (handlerB2B, _, _) = Montar(PlanoB2B);
        var (handlerVazio, _, _) = Montar();

        var audienciaErrada = await handlerB2B.HandleAsync(
            Comando(plano: "chms_basic"), CancellationToken.None);
        var inexistente = await handlerVazio.HandleAsync(
            Comando(plano: "nao_existe"), CancellationToken.None);

        Assert.Equal(audienciaErrada.Outcome, inexistente.Outcome);
        Assert.Equal(audienciaErrada.Detail, inexistente.Detail);
    }

    [Fact]
    public async Task O_pagamento_nasce_pendente_e_ligado_a_uma_assinatura()
    {
        // `subscription_id` nulo faria o GrantEntitlementHandler registrar
        // "pagamento sem assinatura" e não conceder nada: o usuário pagaria e
        // não receberia acesso.
        var (handler, payments, _) = Montar(PlanoB2C);

        await handler.HandleAsync(Comando(), CancellationToken.None);

        var pagamento = Assert.Single(payments.Todos);
        Assert.Equal(PaymentStatus.Pending, pagamento.Status);
        Assert.NotNull(pagamento.SubscriptionId);
        Assert.NotEqual(0, pagamento.SubscriptionId);
        Assert.Equal(Titular, pagamento.UserId);
    }

    [Fact]
    public async Task Repassa_a_chave_ao_gateway()
    {
        // Idempotência só do nosso lado não impede a segunda chamada de virar a
        // segunda cobrança lá dentro.
        var (handler, _, gateway) = Montar(PlanoB2C);

        await handler.HandleAsync(Comando(chave: "abc"), CancellationToken.None);

        Assert.Contains("abc", gateway.UltimaCobranca!.IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains(Titular.ToString(System.Globalization.CultureInfo.InvariantCulture),
            gateway.UltimaCobranca.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nao_grava_pagamento_quando_o_gateway_falha()
    {
        // A ordem inversa deixaria uma linha de pagamento sem cobrança
        // correspondente — órfã, que webhook nenhum resolveria.
        var (handler, payments, gateway) = Montar(PlanoB2C);
        gateway.Falhar = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(Comando(), CancellationToken.None));

        Assert.Empty(payments.Todos);
    }

    [Fact]
    public async Task Devolve_a_cobranca_vencedora_quando_a_constraint_acusa_corrida()
    {
        // Duas requisições simultâneas passam pela consulta prévia antes de
        // qualquer INSERT. Quem decide é a constraint — e o perdedor precisa
        // devolver a cobrança do vencedor, não um erro.
        var payments = new FakePaymentRepository();
        var gateway = new FakeGateway();

        var handler = new StartCheckoutHandler(
            new FakePlanRepository([PlanoB2C]),
            payments,
            new FakeSubscriptionStore(),
            gateway,
            new FakeUnitOfWork(payments) { SimularCorrida = true },
            new FakeTimeProvider(Agora),
            NullLogger<StartCheckoutHandler>.Instance);

        var resultado = await handler.HandleAsync(Comando(), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.Reused, resultado.Outcome);
        Assert.NotEqual(Guid.Empty, resultado.PaymentId);
    }

    [Fact]
    public async Task Recusa_checkout_quando_ja_ha_assinatura_de_outro_plano_em_andamento()
    {
        // uq_sub_active_user permite só uma assinatura não-terminal por
        // pessoa. FindReusableForCheckoutAsync filtra pelo plano PEDIDO, então
        // não encontra a existente (de outro plano) — quem precisa barrar é a
        // constraint, achada aqui por um 500 real na primeira vez que alguém
        // tentou um segundo plano com o primeiro ainda pendente.
        var payments = new FakePaymentRepository();
        var gateway = new FakeGateway();

        var handler = new StartCheckoutHandler(
            new FakePlanRepository([PlanoB2C]),
            payments,
            new FakeSubscriptionStore(),
            gateway,
            new FakeUnitOfWork(payments) { SimularConflitoDeAssinatura = true },
            new FakeTimeProvider(Agora),
            NullLogger<StartCheckoutHandler>.Instance);

        var resultado = await handler.HandleAsync(Comando(), CancellationToken.None);

        Assert.Equal(CheckoutOutcome.SubscriptionConflict, resultado.Outcome);
        Assert.Empty(payments.Todos);
        Assert.Equal(0, gateway.Chamadas);
    }

    // -------------------------------------------------------------------------
    // Dublês
    // -------------------------------------------------------------------------

    private sealed class FakePlanRepository(IReadOnlyList<PlanSnapshot> planos) : IPlanRepository
    {
        public Task<PlanSnapshot?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(planos.FirstOrDefault(p => p.Code == code));

        public Task<PlanSnapshot?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(planos.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<PlanSnapshot>> ListActiveAsync(
            PlanAudience audience, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlanSnapshot>>(
                planos.Where(p => p.Audience == audience).ToList());
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<Payment> _persistidos = [];
        private readonly List<Payment> _pendentes = [];

        public List<Payment> Todos => _persistidos;

        public void Add(Payment payment) => _pendentes.Add(payment);

        /// <summary>Simula o commit: só depois disso a chave passa a ser vista.</summary>
        public void Commit()
        {
            _persistidos.AddRange(_pendentes);
            _pendentes.Clear();
        }

        /// <summary>Há pagamento aguardando commit? Distingue o commit da assinatura do dele.</summary>
        public bool TemPagamentoPendente => _pendentes.Count > 0;

        public Task<Payment?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(_persistidos.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey));

        public Task<Payment?> FindByGatewayChargeIdAsync(string gatewayChargeId, CancellationToken cancellationToken) =>
            Task.FromResult(_persistidos.FirstOrDefault(p => p.GatewayChargeId == gatewayChargeId));

        public Task<Payment?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
            Task.FromResult(_persistidos.FirstOrDefault(p => p.PublicId == publicId));

        public Task<IReadOnlyList<Payment>> ListByUserAsync(
            long userId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Payment>>(
                _persistidos.Where(p => p.UserId == userId).Take(limit).ToList());
    }

    private sealed class FakeSubscriptionStore : ISubscriptionStore
    {
        private readonly List<Subscription> _todas = [];
        private long _proximoId = 1;

        public Task<Subscription?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(_todas.FirstOrDefault(s => s.Id == id));

        public Task<Subscription?> FindCurrentByUserAsync(long userId, CancellationToken cancellationToken) =>
            Task.FromResult(_todas.FirstOrDefault(
                s => s.UserId == userId && s.Status == SubscriptionStatus.Active));

        public Task<Subscription?> FindReusableForCheckoutAsync(
            long userId, long planId, CancellationToken cancellationToken) =>
            Task.FromResult(_todas.FirstOrDefault(s => s.UserId == userId && s.PlanId == planId));

        public void Add(Subscription subscription)
        {
            // O EF atribui o Id no commit; aqui o dublê faz o mesmo, senão o
            // teste de "pagamento ligado a assinatura" passaria por engano com 0.
            typeof(Subscription)
                .GetProperty(nameof(Subscription.Id))!
                .SetValue(subscription, _proximoId++);

            _todas.Add(subscription);
        }
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public int Chamadas { get; private set; }
        public ChargeRequest? UltimaCobranca { get; private set; }
        public bool Falhar { get; set; }

        public Task<ChargeResult> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
        {
            if (Falhar)
            {
                throw new InvalidOperationException("Gateway indisponível.");
            }

            Chamadas++;
            UltimaCobranca = request;

            return Task.FromResult(new ChargeResult
            {
                ChargeId = $"chg_{request.IdempotencyKey}",
                Status = GatewayChargeStatus.Pending,
                CheckoutUrl = "https://exemplo.test/pagar",
            });
        }

        public Task<ChargeSnapshot?> FetchChargeAsync(string chargeId, CancellationToken cancellationToken) =>
            Task.FromResult<ChargeSnapshot?>(null);
    }

    private sealed class FakeUnitOfWork(FakePaymentRepository payments) : IUnitOfWork
    {
        private bool _jaFalhou;

        /// <summary>Faz o commit do pagamento colidir uma vez, como a constraint faria.</summary>
        public bool SimularCorrida { get; init; }

        /// <summary>
        /// Faz o PRIMEIRO commit (o da assinatura nova, dentro de
        /// ResolverAssinaturaAsync) colidir com uq_sub_active_user — como
        /// aconteceria se o titular já tivesse uma assinatura não-terminal de
        /// outro plano.
        /// </summary>
        public bool SimularConflitoDeAssinatura { get; init; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (SimularConflitoDeAssinatura && !_jaFalhou)
            {
                _jaFalhou = true;
                throw new UniqueConstraintViolationException(
                    "uq_sub_active_user", new InvalidOperationException("23505"));
            }

            // Só o commit DO PAGAMENTO pode colidir na chave de idempotência —
            // o commit anterior, da assinatura, não passa por essa constraint.
            if (SimularCorrida && !_jaFalhou && payments.TemPagamentoPendente)
            {
                _jaFalhou = true;

                // O vencedor da corrida gravou entre a consulta prévia e este
                // INSERT: a linha com a mesma chave passa a existir, e é ela que
                // a releitura do handler precisa encontrar.
                payments.Commit();

                throw new UniqueConstraintViolationException(
                    "uq_pay_idempotency_key", new InvalidOperationException("23505"));
            }

            payments.Commit();
            return Task.FromResult(1);
        }
    }
}
