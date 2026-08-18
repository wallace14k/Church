namespace Congrega.Domain.Billing;

public interface IPaymentRepository
{
    /// <summary>
    /// Busca pela chave de idempotência do checkout.
    /// </summary>
    /// <remarks>
    /// Usado para <b>devolver</b> a cobrança já criada quando o app reenvia o
    /// mesmo checkout, não para decidir se pode criar: essa decisão é da
    /// constraint <c>uq_pay_idempotency_key</c>. A consulta aqui é atalho para o
    /// caminho feliz; a corrida é resolvida no banco.
    /// </remarks>
    Task<Payment?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Busca pelo identificador da cobrança no gateway — o caminho do webhook.</summary>
    Task<Payment?> FindByGatewayChargeIdAsync(string gatewayChargeId, CancellationToken cancellationToken);

    Task<Payment?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    void Add(Payment payment);
}

/// <summary>
/// Acesso ao agregado de assinatura para o fluxo de cobrança.
/// </summary>
/// <remarks>
/// Separado do <c>ISubscriptionRepository</c> de <c>Congrega.Domain.Retention</c>
/// de propósito: aquele é uma consulta especializada em varrer candidatos a
/// alerta, com conexão direta e paginação por keyset. Juntar as duas
/// responsabilidades numa interface só faria o motor de retenção carregar
/// agregados que ele não usa, e o fluxo de cobrança herdar uma assinatura de
/// método pensada para varredura em lote.
/// </remarks>
public interface ISubscriptionStore
{
    Task<Subscription?> FindByIdAsync(long id, CancellationToken cancellationToken);

    Task<Subscription?> FindActiveByUserAsync(long userId, CancellationToken cancellationToken);

    /// <summary>
    /// Assinatura do usuário para aquele plano que um novo checkout deve
    /// reaproveitar em vez de duplicar.
    /// </summary>
    /// <remarks>
    /// Inclui <c>Pending</c>, ao contrário de <see cref="FindActiveByUserAsync"/>:
    /// uma tentativa de checkout que falhe no gateway deixa a assinatura
    /// pendente para trás, e sem reaproveitá-la cada retry criaria mais uma.
    /// Pendente não concede acesso — só o pagamento confirmado a ativa —, então
    /// reusá-la não antecipa nada.
    /// </remarks>
    Task<Subscription?> FindReusableForCheckoutAsync(
        long userId,
        long planId,
        CancellationToken cancellationToken);

    void Add(Subscription subscription);
}

public interface IEntitlementRepository
{
    /// <summary>
    /// Direitos ativos do usuário no instante dado.
    /// </summary>
    /// <remarks>
    /// É a consulta que responde "esta pessoa pode ver este conteúdo". Filtra
    /// revogados e vencidos <b>no banco</b>: trazer tudo e filtrar em memória
    /// faria a checagem de acesso crescer com o histórico de compras da pessoa.
    /// </remarks>
    Task<IReadOnlyList<Entitlement>> ListActiveAsync(
        long userId,
        DateTimeOffset moment,
        CancellationToken cancellationToken);

    /// <summary>Direito ativo de um plano específico, se houver.</summary>
    Task<Entitlement?> FindActiveForPlanAsync(
        long userId,
        long planId,
        DateTimeOffset moment,
        CancellationToken cancellationToken);

    /// <summary>Direitos originados por uma assinatura — o que o estorno precisa revogar.</summary>
    Task<IReadOnlyList<Entitlement>> ListBySubscriptionAsync(
        long subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>Direitos originados por um pagamento específico.</summary>
    Task<IReadOnlyList<Entitlement>> ListByPaymentAsync(
        long paymentId,
        CancellationToken cancellationToken);

    void Add(Entitlement entitlement);
}

/// <summary>Provedor do webhook. Espelha <c>payment_webhooks.provider</c>.</summary>
public enum WebhookProvider : short
{
    AbacatePay = 1,
    AppleAppStore = 2,
    GooglePlay = 3,
}

/// <summary>Um evento cru recebido do gateway, guardado antes de ser processado.</summary>
public sealed record ReceivedWebhook
{
    public required WebhookProvider Provider { get; init; }

    /// <summary>
    /// Identificador do evento no provedor. Compõe a chave única
    /// <c>uq_webhook_event (provider, provider_event_id)</c> — é ela, e não um
    /// lock distribuído, que garante o processamento único.
    /// </summary>
    public required string ProviderEventId { get; init; }

    public required string EventType { get; init; }
    public required string Payload { get; init; }

    /// <summary>
    /// Assinatura conferida.
    /// </summary>
    /// <remarks>
    /// Guardado mesmo quando <c>false</c>: um evento com assinatura inválida é
    /// justamente o que se quer poder investigar depois. Descartá-lo na porta
    /// apagaria a evidência de uma tentativa de forjar pagamento.
    /// </remarks>
    public required bool SignatureValid { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>Um evento já registrado e com assinatura válida, pronto para processar.</summary>
/// <remarks>
/// Deliberadamente sem <c>SignatureHeader</c>: quem reivindica esta linha não
/// reverifica a assinatura (isso já aconteceu na borda, e o cabeçalho original
/// nem é guardado) — só o payload, para extrair o identificador da cobrança.
/// </remarks>
public sealed record PendingPaymentWebhook
{
    public required WebhookProvider Provider { get; init; }
    public required string ProviderEventId { get; init; }
    public required string Payload { get; init; }
}

public interface IPaymentWebhookRepository
{
    /// <summary>
    /// Registra o evento cru. Devolve <c>false</c> quando já existia — é a
    /// deduplicação, e ela vem da constraint única, não de uma consulta prévia.
    /// </summary>
    Task<bool> TryRecordAsync(ReceivedWebhook webhook, CancellationToken cancellationToken);

    /// <summary>
    /// Reivindica um lote de eventos pendentes para o worker processar.
    /// </summary>
    /// <remarks>
    /// Só eventos com assinatura válida entram no lote — um evento forjado fica
    /// registrado para auditoria, mas nunca é processado. <paramref
    /// name="maxAttempts"/> tira da fila um evento cuja cobrança nunca resolve
    /// (bug, gateway fora do ar) depois de tentativas suficientes, para não
    /// martelar o gateway para sempre.
    /// </remarks>
    Task<IReadOnlyList<PendingPaymentWebhook>> ClaimBatchAsync(
        int batchSize,
        short maxAttempts,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        WebhookProvider provider,
        string providerEventId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        WebhookProvider provider,
        string providerEventId,
        string failureReason,
        CancellationToken cancellationToken);
}

/// <summary>Espelha <c>plans.audience</c>. Decide QUEM pode assinar o plano.</summary>
/// <remarks>
/// Não é rótulo de catálogo: é controle. Sem ele, um assinante Congrega+ poderia
/// abrir checkout do plano do ChMS — cobrado da igreja, com preço e direitos
/// diferentes — só informando o código dele. O checkout confere a audiência
/// contra o titular antes de criar qualquer cobrança.
/// </remarks>
public enum PlanAudience : short
{
    /// <summary>Plano de igreja (ChMS B2B). Titular é o tenant.</summary>
    Tenant = 1,

    /// <summary>Plano Congrega+ (B2C). Titular é a pessoa.</summary>
    User = 2,
}

/// <summary>Dados do plano necessários para cobrar e conceder acesso.</summary>
public sealed record PlanSnapshot
{
    public required long Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required long PriceCents { get; init; }
    public required PlanAudience Audience { get; init; }
    /// <summary>1=Mensal 2=Anual, conforme <c>plans.billing_period</c>.</summary>
    public required short BillingPeriod { get; init; }
}

public interface IPlanRepository
{
    Task<PlanSnapshot?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    Task<PlanSnapshot?> FindByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Catálogo ativo para uma audiência — o que a tela de escolha de plano mostra.</summary>
    Task<IReadOnlyList<PlanSnapshot>> ListActiveAsync(PlanAudience audience, CancellationToken cancellationToken);
}
