using Congrega.Domain.Common;

namespace Congrega.Domain.Billing;

/// <summary>Estados possíveis de uma assinatura. Ver docs/03-arquitetura.md §6.</summary>
public enum SubscriptionStatus
{
    Pending = 1,
    Active = 2,
    PastDue = 3,
    Grace = 4,
    Canceled = 5,
    Expired = 6
}

/// <summary>
/// Origem da assinatura. O domínio conhece a origem como um dado, nunca como uma
/// bifurcação de lógica — é o que permite reconciliar Abacate.pay, Apple e Google
/// em um único modelo (ADR-009).
/// </summary>
public enum SubscriptionSource
{
    AbacatePay = 1,
    AppleAppStore = 2,
    GooglePlay = 3,
    Courtesy = 4
}

public sealed record SubscriptionActivated(long SubscriptionId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SubscriptionEnteredGrace(
    long SubscriptionId,
    DateTimeOffset GraceUntil,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record SubscriptionExpired(long SubscriptionId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Transição de estado inválida na máquina de estados da assinatura.</summary>
public sealed class InvalidSubscriptionTransitionException(
    SubscriptionStatus from,
    SubscriptionStatus to)
    : InvalidOperationException($"Transição inválida de {from} para {to}.")
{
    public SubscriptionStatus From { get; } = from;
    public SubscriptionStatus To { get; } = to;
}

/// <summary>
/// Agregado de assinatura. Concentra a máquina de estados; nenhum handler,
/// controller ou worker altera <see cref="Status"/> diretamente.
/// </summary>
/// <remarks>
/// Webhook fora de ordem é a norma, não a exceção — provedores reentregam e
/// reordenam. Validar a transição aqui é o que impede que um evento atrasado de
/// "pagamento confirmado" reative uma assinatura já expirada e conceda acesso
/// indevido.
/// </remarks>
public sealed class Subscription : AggregateRoot
{
    // Transições permitidas. Tabela explícita em vez de uma cadeia de ifs: fica
    // legível, testável e é o próprio documento da regra.
    private static readonly Dictionary<SubscriptionStatus, SubscriptionStatus[]> AllowedTransitions = new()
    {
        [SubscriptionStatus.Pending] = [SubscriptionStatus.Active, SubscriptionStatus.Expired],
        [SubscriptionStatus.Active] = [SubscriptionStatus.PastDue, SubscriptionStatus.Canceled, SubscriptionStatus.Active],
        [SubscriptionStatus.PastDue] = [SubscriptionStatus.Active, SubscriptionStatus.Grace, SubscriptionStatus.Canceled],
        [SubscriptionStatus.Grace] = [SubscriptionStatus.Active, SubscriptionStatus.Expired],
        [SubscriptionStatus.Canceled] = [SubscriptionStatus.Active, SubscriptionStatus.Expired],
        [SubscriptionStatus.Expired] = []
    };

    private Subscription()
    {
        // Construtor sem parâmetros exigido pelo EF Core para materialização.
    }

    public long Id { get; private set; }
    public long PlanId { get; private set; }
    public long? TenantId { get; private set; }
    public long? UserId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public SubscriptionSource Source { get; private set; }
    public string? ExternalId { get; private set; }
    public DateTimeOffset CurrentPeriodStart { get; private set; }
    public DateTimeOffset CurrentPeriodEnd { get; private set; }
    public DateTimeOffset? GraceUntil { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }

    public static Subscription Create(
        long planId,
        long? tenantId,
        long? userId,
        SubscriptionSource source,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        // Espelha o CHECK ck_sub_owner do banco. A regra vive nos dois lugares de
        // propósito: no domínio para falhar cedo com mensagem útil, no banco para
        // que nenhum caminho alternativo (script, migração, import) a contorne.
        if ((tenantId is null) == (userId is null))
        {
            throw new ArgumentException(
                "Uma assinatura pertence a um tenant OU a um usuário, nunca a ambos ou a nenhum.");
        }

        if (periodEnd <= periodStart)
        {
            throw new ArgumentException("O fim do período deve ser posterior ao início.", nameof(periodEnd));
        }

        return new Subscription
        {
            PlanId = planId,
            TenantId = tenantId,
            UserId = userId,
            Source = source,
            Status = SubscriptionStatus.Pending,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd
        };
    }

    public void Activate(DateTimeOffset now)
    {
        EnsureCanTransitionTo(SubscriptionStatus.Active);
        Status = SubscriptionStatus.Active;
        GraceUntil = null;
        Raise(new SubscriptionActivated(Id, now));
    }

    public void Renew(DateTimeOffset newPeriodEnd, DateTimeOffset now)
    {
        EnsureCanTransitionTo(SubscriptionStatus.Active);

        if (newPeriodEnd <= CurrentPeriodEnd)
        {
            throw new ArgumentException(
                "A renovação precisa estender o período atual.", nameof(newPeriodEnd));
        }

        CurrentPeriodStart = CurrentPeriodEnd;
        CurrentPeriodEnd = newPeriodEnd;
        Status = SubscriptionStatus.Active;
        GraceUntil = null;
        Raise(new SubscriptionActivated(Id, now));
    }

    public void MarkPastDue()
    {
        EnsureCanTransitionTo(SubscriptionStatus.PastDue);
        Status = SubscriptionStatus.PastDue;
    }

    public void EnterGrace(DateTimeOffset graceUntil, DateTimeOffset now)
    {
        EnsureCanTransitionTo(SubscriptionStatus.Grace);
        Status = SubscriptionStatus.Grace;
        GraceUntil = graceUntil;
        Raise(new SubscriptionEnteredGrace(Id, graceUntil, now));
    }

    /// <summary>
    /// Cancela a assinatura. <b>Não revoga acesso.</b> O usuário pagou até
    /// <see cref="CurrentPeriodEnd"/> e os entitlements permanecem válidos até lá —
    /// confundir "cancelou" com "perdeu acesso" gera reclamação e chargeback.
    /// </summary>
    public void Cancel(DateTimeOffset now, bool immediate = false)
    {
        EnsureCanTransitionTo(SubscriptionStatus.Canceled);
        Status = SubscriptionStatus.Canceled;
        CanceledAt = now;
        CancelAtPeriodEnd = !immediate;

        if (immediate)
        {
            CurrentPeriodEnd = now;
        }
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureCanTransitionTo(SubscriptionStatus.Expired);
        Status = SubscriptionStatus.Expired;
        Raise(new SubscriptionExpired(Id, now));
    }

    /// <summary>Elegível a alerta de retenção apenas nos estados em que renovar ainda faz sentido.</summary>
    public bool IsEligibleForRetentionAlerts() =>
        Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Grace;

    private void EnsureCanTransitionTo(SubscriptionStatus target)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(target))
        {
            throw new InvalidSubscriptionTransitionException(Status, target);
        }
    }
}
