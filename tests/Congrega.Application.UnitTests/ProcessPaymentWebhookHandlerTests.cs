using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Congrega.Application.UnitTests;

/// <summary>
/// Fetch-on-notify: o worker resolve um webhook já registrado contra o estado
/// real da cobrança.
/// </summary>
/// <remarks>
/// Cada teste cobre um ramo da máquina de estados ou um caso em que o evento
/// não pode ser resolvido — e nenhum deles pode propagar exceção: uma cobrança
/// com erro nunca deve derrubar o lote inteiro que o dispatcher está drenando.
/// </remarks>
public sealed class ProcessPaymentWebhookHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static (ProcessPaymentWebhookHandler Handler, FakeWebhookRepository Webhooks,
        FakePaymentRepository Payments, FakeGateway Gateway, FakeUnitOfWork UnitOfWork) Montar()
    {
        var webhooks = new FakeWebhookRepository();
        var payments = new FakePaymentRepository();
        var gateway = new FakeGateway();
        var unitOfWork = new FakeUnitOfWork();

        var handler = new ProcessPaymentWebhookHandler(
            webhooks,
            payments,
            gateway,
            unitOfWork,
            new FakeTimeProvider(Agora),
            NullLogger<ProcessPaymentWebhookHandler>.Instance);

        return (handler, webhooks, payments, gateway, unitOfWork);
    }

    private static Payment PagamentoPendente(string chargeId)
    {
        var pagamento = Payment.Start(
            amountCents: 2990,
            idempotencyKey: "chave-1",
            source: SubscriptionSource.AbacatePay,
            now: Agora,
            userId: 42);

        pagamento.AttachGatewayCharge(chargeId, Agora);
        return pagamento;
    }

    private static PendingPaymentWebhook Evento(string eventId, string? chargeId) => new()
    {
        Provider = WebhookProvider.AbacatePay,
        ProviderEventId = eventId,
        Payload = chargeId is null
            ? $$"""{"event_id":"{{eventId}}","event_type":"ping"}"""
            : $$"""{"event_id":"{{eventId}}","event_type":"charge.updated","charge_id":"{{chargeId}}"}""",
    };

    [Fact]
    public async Task Cobranca_paga_confirma_o_pagamento()
    {
        var (handler, webhooks, payments, gateway, unitOfWork) = Montar();
        var pagamento = PagamentoPendente("chg_1");
        payments.Registrar("chg_1", pagamento);
        gateway.Resposta = new ChargeSnapshot
        {
            ChargeId = "chg_1",
            Status = GatewayChargeStatus.Paid,
            AmountCents = 2990,
            PaidAt = Agora,
        };

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_1"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Processed, resultado.Outcome);
        Assert.Equal(PaymentStatus.Paid, pagamento.Status);
        Assert.Single(webhooks.Processados);
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task Cobranca_recusada_marca_o_pagamento_como_falho()
    {
        var (handler, webhooks, payments, gateway, unitOfWork) = Montar();
        var pagamento = PagamentoPendente("chg_1");
        payments.Registrar("chg_1", pagamento);
        gateway.Resposta = new ChargeSnapshot
        {
            ChargeId = "chg_1",
            Status = GatewayChargeStatus.Failed,
            AmountCents = 2990,
            FailureReason = "Cartão recusado.",
        };

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_1"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Processed, resultado.Outcome);
        Assert.Equal(PaymentStatus.Failed, pagamento.Status);
        Assert.Equal(1, unitOfWork.Commits);
    }

    [Fact]
    public async Task Cobranca_estornada_revoga_um_pagamento_ja_pago()
    {
        var (handler, webhooks, payments, gateway, unitOfWork) = Montar();
        var pagamento = PagamentoPendente("chg_1");
        pagamento.Confirm(Agora, Agora);
        payments.Registrar("chg_1", pagamento);
        gateway.Resposta = new ChargeSnapshot
        {
            ChargeId = "chg_1",
            Status = GatewayChargeStatus.Refunded,
            AmountCents = 2990,
        };

        var resultado = await handler.HandleAsync(Evento("evt_2", "chg_1"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Processed, resultado.Outcome);
        Assert.Equal(PaymentStatus.Refunded, pagamento.Status);
        // Um commit no Confirm() de preparo do teste não existe — só o do handler.
        Assert.Equal(1, unitOfWork.Commits);
        Assert.Single(webhooks.Processados);
    }

    [Fact]
    public async Task Cobranca_ainda_pendente_nao_muda_o_pagamento_nem_comita()
    {
        var (handler, webhooks, payments, gateway, unitOfWork) = Montar();
        var pagamento = PagamentoPendente("chg_1");
        payments.Registrar("chg_1", pagamento);
        gateway.Resposta = new ChargeSnapshot
        {
            ChargeId = "chg_1",
            Status = GatewayChargeStatus.Pending,
            AmountCents = 2990,
        };

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_1"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Processed, resultado.Outcome);
        Assert.Equal(PaymentStatus.Pending, pagamento.Status);
        Assert.Equal(0, unitOfWork.Commits);
        Assert.Single(webhooks.Processados);
    }

    [Fact]
    public async Task Cobranca_desconhecida_no_provedor_e_ignorada_e_marcada_processada()
    {
        // O evento não vai virar cobrança nenhuma dia nenhum: marcar como
        // processado (não como falha) é o que impede reivindicação eterna.
        var (handler, webhooks, _, gateway, unitOfWork) = Montar();
        gateway.Resposta = null;

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_fantasma"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Ignored, resultado.Outcome);
        Assert.Single(webhooks.Processados);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task Cobranca_sem_pagamento_local_e_ignorada()
    {
        // Pode ser de outro ambiente batendo na mesma conta de sandbox. Mais
        // seguro ignorar do que inventar um pagamento sem titular.
        var (handler, webhooks, _, gateway, _) = Montar();
        gateway.Resposta = new ChargeSnapshot
        {
            ChargeId = "chg_orfao",
            Status = GatewayChargeStatus.Paid,
            AmountCents = 2990,
        };

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_orfao"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Ignored, resultado.Outcome);
        Assert.Single(webhooks.Processados);
    }

    [Fact]
    public async Task Evento_sem_cobranca_associada_e_ignorado()
    {
        var (handler, webhooks, _, _, _) = Montar();

        var resultado = await handler.HandleAsync(Evento("evt_1", chargeId: null), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Ignored, resultado.Outcome);
        Assert.Single(webhooks.Processados);
    }

    [Fact]
    public async Task Falha_do_gateway_marca_falho_sem_lancar_e_sem_comitar()
    {
        // Isolamento de "cobrança venenosa": o dispatcher precisa seguir para o
        // próximo item do lote, não abortar o ciclo inteiro.
        var (handler, webhooks, payments, gateway, unitOfWork) = Montar();
        payments.Registrar("chg_1", PagamentoPendente("chg_1"));
        gateway.Falhar = true;

        var resultado = await handler.HandleAsync(Evento("evt_1", "chg_1"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Failed, resultado.Outcome);
        Assert.Single(webhooks.Falhados);
        Assert.Empty(webhooks.Processados);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task Payload_ilegivel_ao_reprocessar_marca_falho_sem_lancar()
    {
        // Não deveria acontecer — o mesmo payload já foi parseado com sucesso
        // para chegar à tabela — mas se acontecer, não pode travar o lote.
        var (handler, webhooks, _, _, _) = Montar();

        var resultado = await handler.HandleAsync(
            new PendingPaymentWebhook
            {
                Provider = WebhookProvider.AbacatePay,
                ProviderEventId = "evt_x",
                Payload = "isto não é JSON",
            },
            CancellationToken.None);

        Assert.Equal(WebhookOutcome.Failed, resultado.Outcome);
        Assert.Single(webhooks.Falhados);
        Assert.Empty(webhooks.Processados);
    }

    // -------------------------------------------------------------------------
    // Dublês
    // -------------------------------------------------------------------------

    private sealed class FakeWebhookRepository : IPaymentWebhookRepository
    {
        public List<(WebhookProvider Provider, string EventId)> Processados { get; } = [];
        public List<(WebhookProvider Provider, string EventId, string Erro)> Falhados { get; } = [];

        public Task<bool> TryRecordAsync(ReceivedWebhook webhook, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Só a borda registra — o processador opera sobre o que já foi registrado.");

        public Task<IReadOnlyList<PendingPaymentWebhook>> ClaimBatchAsync(
            int batchSize, short maxAttempts, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Reivindicação é responsabilidade do dispatcher, não do handler.");

        public Task MarkProcessedAsync(
            WebhookProvider provider, string providerEventId, DateTimeOffset processedAt,
            CancellationToken cancellationToken)
        {
            Processados.Add((provider, providerEventId));
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            WebhookProvider provider, string providerEventId, string failureReason,
            CancellationToken cancellationToken)
        {
            Falhados.Add((provider, providerEventId, failureReason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Dictionary<string, Payment> _porChargeId = new(StringComparer.Ordinal);

        public void Registrar(string chargeId, Payment payment) => _porChargeId[chargeId] = payment;

        public Task<Payment?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Payment?> FindByGatewayChargeIdAsync(string gatewayChargeId, CancellationToken cancellationToken) =>
            Task.FromResult(_porChargeId.GetValueOrDefault(gatewayChargeId));

        public Task<Payment?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Payment>> ListByUserAsync(
            long userId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException("O processador de webhook resolve por cobrança, nunca por titular.");

        public void Add(Payment payment) => throw new NotSupportedException();
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public ChargeSnapshot? Resposta { get; set; }
        public bool Falhar { get; set; }

        public Task<ChargeResult> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChargeSnapshot?> FetchChargeAsync(string chargeId, CancellationToken cancellationToken)
        {
            if (Falhar)
            {
                throw new InvalidOperationException("Gateway indisponível.");
            }

            return Task.FromResult(Resposta);
        }
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
