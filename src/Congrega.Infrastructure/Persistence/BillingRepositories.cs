using Congrega.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(p => p.PublicId).HasColumnName("public_id");
        builder.Property(p => p.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(p => p.UserId).HasColumnName("user_id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id");
        builder.Property(p => p.AmountCents).HasColumnName("amount_cents");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(p => p.Method).HasColumnName("method").HasConversion<short?>();
        builder.Property(p => p.Source).HasColumnName("source").HasConversion<short>();
        builder.Property(p => p.GatewayChargeId).HasColumnName("gateway_charge_id").HasMaxLength(200);
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.FailedAt).HasColumnName("failed_at");
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason").HasMaxLength(300);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(p => p.PublicId).IsUnique();
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();

        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        builder.ToTable("entitlements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.PlanId).HasColumnName("plan_id");
        builder.Property(e => e.ResourcePackId).HasColumnName("resource_pack_id");
        builder.Property(e => e.Source).HasColumnName("source").HasConversion<short>();
        builder.Property(e => e.SourceSubscriptionId).HasColumnName("source_subscription_id");
        builder.Property(e => e.SourcePaymentId).HasColumnName("source_payment_id");
        builder.Property(e => e.GrantedAt).HasColumnName("granted_at");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.RevokedReason).HasColumnName("revoked_reason").HasConversion<short?>();
        builder.Property(e => e.GrantedBy).HasColumnName("granted_by");
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(300);

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class PaymentRepository(CongregaDbContext db) : IPaymentRepository
{
    public Task<Payment?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<Payment?> FindByGatewayChargeIdAsync(
        string gatewayChargeId,
        CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.GatewayChargeId == gatewayChargeId, cancellationToken);

    public Task<Payment?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.PublicId == publicId, cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}

internal sealed class EntitlementRepository(CongregaDbContext db) : IEntitlementRepository
{
    public async Task<IReadOnlyList<Entitlement>> ListActiveAsync(
        long userId,
        DateTimeOffset moment,
        CancellationToken cancellationToken) =>
        // As duas condições no banco. Filtrar em memória faria a checagem de
        // acesso crescer com o histórico de compras da pessoa.
        await db.Entitlements
            .AsNoTracking()
            .Where(e => e.UserId == userId
                && e.RevokedAt == null
                && (e.ExpiresAt == null || e.ExpiresAt > moment))
            .ToListAsync(cancellationToken);

    public Task<Entitlement?> FindActiveForPlanAsync(
        long userId,
        long planId,
        DateTimeOffset moment,
        CancellationToken cancellationToken) =>
        db.Entitlements.FirstOrDefaultAsync(
            e => e.UserId == userId
                && e.PlanId == planId
                && e.RevokedAt == null
                && (e.ExpiresAt == null || e.ExpiresAt > moment),
            cancellationToken);

    public async Task<IReadOnlyList<Entitlement>> ListBySubscriptionAsync(
        long subscriptionId,
        CancellationToken cancellationToken) =>
        await db.Entitlements
            .Where(e => e.SourceSubscriptionId == subscriptionId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Entitlement>> ListByPaymentAsync(
        long paymentId,
        CancellationToken cancellationToken) =>
        // Sem AsNoTracking: o chamador revoga estes agregados, e sem rastreio o
        // SaveChanges não gravaria nada — falha silenciosa, a pior de todas
        // num fluxo de estorno.
        await db.Entitlements
            .Where(e => e.SourcePaymentId == paymentId)
            .ToListAsync(cancellationToken);

    public void Add(Entitlement entitlement) => db.Entitlements.Add(entitlement);
}

internal sealed class SubscriptionStore(CongregaDbContext db) : ISubscriptionStore
{
    public Task<Subscription?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
        db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Subscription?> FindActiveByUserAsync(long userId, CancellationToken cancellationToken) =>
        db.Subscriptions.FirstOrDefaultAsync(
            s => s.UserId == userId
                && (s.Status == SubscriptionStatus.Active
                    || s.Status == SubscriptionStatus.PastDue
                    || s.Status == SubscriptionStatus.Grace),
            cancellationToken);

    public void Add(Subscription subscription) => db.Subscriptions.Add(subscription);
}
