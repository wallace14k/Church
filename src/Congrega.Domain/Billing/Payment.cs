using Congrega.Domain.Common;

namespace Congrega.Domain.Billing;

/// <summary>Espelha <c>payments.status</c>.</summary>
public enum PaymentStatus : short
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
    Chargeback = 5,
}

/// <summary>Espelha <c>payments.method</c>.</summary>
public enum PaymentMethod : short
{
    Pix = 1,
    CreditCard = 2,
    Iap = 3,
    Boleto = 4,
}

public sealed record PaymentConfirmed(
    long PaymentId,
    long? SubscriptionId,
    long? UserId,
    long AmountCents,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PaymentRefunded(
    long PaymentId,
    long? SubscriptionId,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Uma cobrança. Nasce <see cref="PaymentStatus.Pending"/> e só o gateway a move
/// dali.
/// </summary>
/// <remarks>
/// <para>
/// <b>A transição de estado é de mão única.</b> Um pagamento confirmado não
/// volta a pendente, e um estornado não volta a pago. Sem essa trava, um webhook
/// atrasado ou reentregue reabriria uma cobrança já resolvida — e o
/// <c>entitlement</c> concedido por ela seria revogado e reconcedido em
/// looping, conforme a ordem em que os eventos chegassem.
/// </para>
/// <para>
/// <b>Confirmar é idempotente.</b> <see cref="Confirm"/> chamado de novo sobre
/// um pagamento já pago não faz nada e não emite evento — porque webhook
/// duplicado é o caso normal, não a exceção, e a skill de segurança é explícita:
/// "Webhook A, Webhook A duplicado, Webhook A duplicado novamente" precisa
/// resultar em "1 evento processado, 0 pagamentos duplicados".
/// </para>
/// </remarks>
public sealed class Payment : AggregateRoot
{
    private Payment()
    {
        IdempotencyKey = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }

    public long? SubscriptionId { get; private set; }
    public long? UserId { get; private set; }
    public long? TenantId { get; private set; }

    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = "BRL";
    public PaymentStatus Status { get; private set; }
    public PaymentMethod? Method { get; private set; }
    public SubscriptionSource Source { get; private set; }

    /// <summary>Identificador da cobrança no gateway. Nulo até o gateway responder.</summary>
    public string? GatewayChargeId { get; private set; }

    /// <summary>
    /// Chave de idempotência do checkout.
    /// </summary>
    /// <remarks>
    /// Tem <c>UNIQUE</c> no banco (<c>uq_pay_idempotency_key</c>). É ela que
    /// impede duas cobranças quando o app reenvia o mesmo checkout por perda de
    /// conexão — e a garantia vem da constraint, não de um <c>if (!existe)</c>,
    /// que sob concorrência tem janela.
    /// </remarks>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Payment Start(
        long amountCents,
        string idempotencyKey,
        SubscriptionSource source,
        DateTimeOffset now,
        long? subscriptionId = null,
        long? userId = null,
        long? tenantId = null,
        PaymentMethod? method = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (amountCents <= 0)
        {
            throw new ArgumentException("O valor da cobrança precisa ser maior que zero.", nameof(amountCents));
        }

        if (userId is null && tenantId is null)
        {
            // Sem titular, o pagamento não tem a quem conceder acesso nem por
            // quem ser cobrado — e a policy de RLS de `payments` filtra
            // justamente por `tenant_id` ou `user_id`. Uma linha sem os dois
            // ficaria invisível para todo mundo.
            throw new ArgumentException(
                "Um pagamento precisa de titular: usuário, igreja, ou os dois.", nameof(userId));
        }

        return new Payment
        {
            PublicId = Guid.NewGuid(),
            AmountCents = amountCents,
            Currency = "BRL",
            Status = PaymentStatus.Pending,
            Source = source,
            Method = method,
            IdempotencyKey = idempotencyKey.Trim(),
            SubscriptionId = subscriptionId,
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Guarda o identificador devolvido pelo gateway ao abrir a cobrança.</summary>
    public void AttachGatewayCharge(string gatewayChargeId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayChargeId);

        if (GatewayChargeId is { } existente && existente != gatewayChargeId)
        {
            // Trocar o identificador apontaria o pagamento para outra cobrança
            // no gateway, e a conciliação passaria a comparar coisas diferentes.
            throw new InvalidOperationException(
                $"Pagamento {Id} já está ligado à cobrança {existente}.");
        }

        GatewayChargeId = gatewayChargeId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Confirma o pagamento. Idempotente: reentrega de webhook não duplica nada.
    /// </summary>
    /// <returns><c>true</c> se esta chamada mudou o estado; <c>false</c> se já estava pago.</returns>
    public bool Confirm(DateTimeOffset paidAt, DateTimeOffset now, PaymentMethod? method = null)
    {
        if (Status == PaymentStatus.Paid)
        {
            return false;
        }

        if (Status is PaymentStatus.Refunded or PaymentStatus.Chargeback)
        {
            throw new InvalidOperationException(
                $"Pagamento {Id} está {Status} e não pode voltar a pago.");
        }

        Status = PaymentStatus.Paid;
        PaidAt = paidAt;
        FailedAt = null;
        FailureReason = null;
        Method = method ?? Method;
        UpdatedAt = now;

        // O evento carrega o que o handler de entitlement precisa, para que ele
        // não tenha de recarregar o agregado só para ler o titular.
        Raise(new PaymentConfirmed(Id, SubscriptionId, UserId, AmountCents, now));
        return true;
    }

    /// <returns><c>true</c> se esta chamada mudou o estado.</returns>
    public bool Fail(string reason, DateTimeOffset now)
    {
        if (Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.Chargeback)
        {
            // Falha que chega depois da confirmação é evento fora de ordem, não
            // uma nova informação. Ignorar é mais seguro que reabrir.
            return false;
        }

        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
        FailedAt = now;
        FailureReason = Truncate(reason, 300);
        UpdatedAt = now;
        return true;
    }

    /// <returns><c>true</c> se esta chamada mudou o estado.</returns>
    public bool Refund(DateTimeOffset now, bool chargeback = false)
    {
        var alvo = chargeback ? PaymentStatus.Chargeback : PaymentStatus.Refunded;

        if (Status == alvo)
        {
            return false;
        }

        if (Status != PaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Só é possível estornar um pagamento pago. Pagamento {Id} está {Status}.");
        }

        Status = alvo;
        UpdatedAt = now;

        // Quem revoga o acesso é o handler que escuta este evento — o pagamento
        // não conhece entitlements, e não deveria.
        Raise(new PaymentRefunded(Id, SubscriptionId, now));
        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
