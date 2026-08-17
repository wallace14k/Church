using Congrega.Domain.Common;

namespace Congrega.Domain.Billing;

/// <summary>Espelha <c>entitlements.source</c>.</summary>
public enum EntitlementSource : short
{
    Subscription = 1,
    OneOffPurchase = 2,
    Courtesy = 3,
    Iap = 4,
}

/// <summary>Espelha <c>entitlements.revoked_reason</c>.</summary>
public enum RevocationReason : short
{
    SubscriptionEnded = 1,
    Refund = 2,
    Chargeback = 3,
    Admin = 4,
}

/// <summary>
/// O direito de acesso a um plano ou a um pacote avulso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este é o único caminho de autorização de conteúdo</b>, seja a origem
/// assinatura, compra avulsa, cortesia ou IAP — é a terceira das três regras que
/// organizam o produto (ver <c>CLAUDE.md</c>). A claim <c>subscription_tier</c>
/// do JWT é dica de interface e nunca autorização: ela pode estar quinze minutos
/// desatualizada e concederia acesso depois de um cancelamento.
/// </para>
/// <para>
/// Pagamento aprovado <b>não</b> é sinônimo de usuário premium. O pagamento
/// dispara a concessão; quem responde "esta pessoa pode ver este conteúdo" é
/// sempre esta tabela.
/// </para>
/// <para>
/// Revogar não apaga: <see cref="RevokedAt"/> e <see cref="RevokedReason"/>
/// preservam por que o acesso caiu. Apagar a linha faria um estorno e um
/// cancelamento ficarem indistinguíveis na auditoria — e o ADR-015 exige que o
/// histórico financeiro sobreviva à saída do titular.
/// </para>
/// </remarks>
public sealed class Entitlement : AggregateRoot
{
    private Entitlement() { }

    public long Id { get; private set; }
    public long UserId { get; private set; }

    /// <summary>Plano assinado. Exclusivo com <see cref="ResourcePackId"/> — ver <c>ck_ent_scope</c>.</summary>
    public long? PlanId { get; private set; }

    /// <summary>Pacote avulso comprado. Exclusivo com <see cref="PlanId"/>.</summary>
    public long? ResourcePackId { get; private set; }

    public EntitlementSource Source { get; private set; }
    public long? SourceSubscriptionId { get; private set; }
    public long? SourcePaymentId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>
    /// Nulo é acesso vitalício — o caso da compra avulsa e da cortesia sem prazo.
    /// Assinatura sempre tem prazo, e ele acompanha o fim do período pago.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }
    public RevocationReason? RevokedReason { get; private set; }
    public long? GrantedBy { get; private set; }
    public string? Note { get; private set; }

    /// <summary>
    /// O direito vale agora?
    /// </summary>
    /// <remarks>
    /// As duas condições juntas, sempre: revogado <b>e</b> vencido. Checar só
    /// uma delas é o erro que deixa um estornado continuar assistindo até a data
    /// de expiração original.
    /// </remarks>
    public bool IsActiveOn(DateTimeOffset moment) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > moment);

    public static Entitlement GrantForPlan(
        long userId,
        long planId,
        EntitlementSource source,
        DateTimeOffset now,
        DateTimeOffset? expiresAt = null,
        long? subscriptionId = null,
        long? paymentId = null,
        long? grantedBy = null,
        string? note = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);

        if (expiresAt is { } fim && fim <= now)
        {
            // Conceder já vencido é sempre erro de cálculo do chamador, e o
            // efeito seria um acesso que nunca funciona — com o usuário
            // convencido de que pagou por ele.
            throw new ArgumentException(
                "O direito não pode nascer vencido.", nameof(expiresAt));
        }

        return new Entitlement
        {
            UserId = userId,
            PlanId = planId,
            ResourcePackId = null,
            Source = source,
            SourceSubscriptionId = subscriptionId,
            SourcePaymentId = paymentId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            GrantedBy = grantedBy,
            Note = note,
        };
    }

    public static Entitlement GrantForPack(
        long userId,
        long resourcePackId,
        EntitlementSource source,
        DateTimeOffset now,
        long? paymentId = null,
        long? grantedBy = null,
        string? note = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourcePackId);

        return new Entitlement
        {
            UserId = userId,
            PlanId = null,
            ResourcePackId = resourcePackId,
            Source = source,
            SourcePaymentId = paymentId,
            GrantedAt = now,
            // Compra avulsa não vence: quem comprou o pack comprou para sempre.
            ExpiresAt = null,
            GrantedBy = grantedBy,
            Note = note,
        };
    }

    /// <summary>
    /// Estende a validade — é o que a renovação da assinatura faz.
    /// </summary>
    /// <remarks>
    /// Nunca encurta. Se o novo prazo for anterior ao atual, o valor é ignorado:
    /// um webhook de renovação fora de ordem chegando depois de outro mais novo
    /// tiraria dias já pagos do assinante.
    /// </remarks>
    public void ExtendTo(DateTimeOffset newExpiry)
    {
        if (ExpiresAt is null || newExpiry > ExpiresAt)
        {
            ExpiresAt = newExpiry;
        }
    }

    /// <returns><c>true</c> se esta chamada revogou; <c>false</c> se já estava revogado.</returns>
    public bool Revoke(RevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            // Idempotente: dois webhooks de estorno não podem produzir dois
            // registros de revogação com motivos diferentes.
            return false;
        }

        RevokedAt = now;
        RevokedReason = reason;
        return true;
    }
}
