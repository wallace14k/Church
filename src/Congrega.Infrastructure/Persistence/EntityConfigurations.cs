using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Mapeamentos EF Core.
/// </summary>
/// <remarks>
/// Configuração por Fluent API, nunca por Data Annotations no domínio — anotações de
/// persistência dentro das entidades acoplariam o domínio ao EF Core, que é
/// exatamente o que a Clean Architecture está evitando aqui.
/// <para>
/// Todas as chaves são <c>BIGINT GENERATED ALWAYS AS IDENTITY</c>, refletindo a
/// restrição do briefing e o DDL em <c>db/schema.sql</c>.
/// </para>
/// </remarks>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(u => u.PublicId).HasColumnName("public_id");
        builder.Property(u => u.Email).HasColumnName("email").HasColumnType("citext").IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(u => u.EmailVerified).HasColumnName("email_verified");
        builder.Property(u => u.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.AnonymizedAt).HasColumnName("anonymized_at");

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.PublicId).IsUnique();

        builder.Ignore(u => u.DomainEvents);
    }
}

internal sealed class EmailVerificationCodeConfiguration : IEntityTypeConfiguration<EmailVerificationCode>
{
    public void Configure(EntityTypeBuilder<EmailVerificationCode> builder)
    {
        builder.ToTable("email_verification_codes");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(c => c.UserId).HasColumnName("user_id");
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").IsRequired();
        builder.Property(c => c.Purpose).HasColumnName("purpose").HasConversion<short>();
        builder.Property(c => c.AttemptCount).HasColumnName("attempt_count");
        builder.Property(c => c.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.RequestIp).HasColumnName("request_ip");

        builder.HasIndex(c => new { c.UserId, c.ExpiresAt });

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(t => t.UserId).HasColumnName("user_id");
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.FamilyId).HasColumnName("family_id");
        builder.Property(t => t.ParentId).HasColumnName("parent_id");
        builder.Property(t => t.SelectedTenantId).HasColumnName("selected_tenant_id");
        builder.Property(t => t.IssuedAt).HasColumnName("issued_at");
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        builder.Property(t => t.UsedAt).HasColumnName("used_at");
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.RevokedReason).HasColumnName("revoked_reason").HasConversion<short?>();
        builder.Property(t => t.DeviceLabel).HasColumnName("device_label").HasMaxLength(120);
        builder.Property(t => t.IpAddress).HasColumnName("ip_address");

        // Busca por hash é o caminho quente do /auth/refresh: uma requisição por
        // cliente a cada 15 minutos. Único também impede colisão de valor gerado.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);

        builder.Ignore(t => t.DomainEvents);
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(t => t.PublicId).HasColumnName("public_id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasColumnType("citext").IsRequired();
        builder.Property(t => t.Document).HasColumnName("document").HasMaxLength(20);
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(t => t.TimeZone).HasColumnName("timezone").HasMaxLength(50);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.SuspendedAt).HasColumnName("suspended_at");

        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.PublicId).IsUnique();

        builder.Ignore(t => t.DomainEvents);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.TenantId).HasColumnName("tenant_id");
        builder.Property(m => m.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at");
        builder.Property(m => m.LeftAt).HasColumnName("left_at");

        builder.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();

        // Coleção mapeada por campo de apoio: a lista é encapsulada e só muda por
        // GrantRole/RevokeRole. Expor ICollection pública permitiria burlar as regras
        // do agregado com um simples .Add().
        builder.Metadata
            .FindNavigation(nameof(Membership.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(m => m.DomainEvents);
    }
}

internal sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(r => r.MembershipId).HasColumnName("membership_id");
        builder.Property(r => r.RoleId).HasColumnName("role_id");
        builder.Property(r => r.GrantedAt).HasColumnName("granted_at");
        builder.Property(r => r.GrantedByUserId).HasColumnName("granted_by");

        builder.HasIndex(r => new { r.MembershipId, r.RoleId }).IsUnique();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(r => r.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.IsSystem).HasColumnName("is_system");
        builder.Property(r => r.TenantId).HasColumnName("tenant_id");
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(p => p.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(150).IsRequired();

        builder.HasIndex(p => p.Code).IsUnique();
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.Property(rp => rp.RoleId).HasColumnName("role_id");
        builder.Property(rp => rp.PermissionId).HasColumnName("permission_id");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(m => m.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at");
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        builder.Property(m => m.Attempts).HasColumnName("attempts");
        builder.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(m => m.LastError).HasColumnName("last_error");
        builder.Property(m => m.CorrelationId).HasColumnName("correlation_id").HasMaxLength(40);
    }
}
