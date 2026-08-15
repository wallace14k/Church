using Congrega.Domain.Common;

namespace Congrega.Domain.Tenancy;

public enum TenantStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    Canceled = 4
}

public enum MembershipStatus
{
    Active = 1,
    Inactive = 2,
    Revoked = 3
}

/// <summary>A igreja. Unidade de isolamento do ChMS.</summary>
public sealed class Tenant : AggregateRoot
{
    private Tenant()
    {
        Name = string.Empty;
        Slug = string.Empty;
        TimeZone = "America/Sao_Paulo";
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string? Document { get; private set; }
    public TenantStatus Status { get; private set; }
    public string TimeZone { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Tenant Create(string name, string slug, DateTimeOffset now, string? document = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return new Tenant
        {
            PublicId = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Document = document,
            Status = TenantStatus.Trial,
            TimeZone = "America/Sao_Paulo",
            CreatedAt = now
        };
    }

    /// <summary>
    /// Tenant suspenso não autentica ninguém. É o corte de acesso por inadimplência —
    /// e precisa ser verificado no login, não apenas na tela de cobrança.
    /// </summary>
    public bool AllowsAccess() => Status is TenantStatus.Trial or TenantStatus.Active;

    public void Suspend(DateTimeOffset now)
    {
        Status = TenantStatus.Suspended;
        SuspendedAt = now;
    }

    public void Reactivate()
    {
        Status = TenantStatus.Active;
        SuspendedAt = null;
    }
}

/// <summary>
/// Vínculo entre uma identidade global e um tenant.
/// </summary>
/// <remarks>
/// <para>
/// É aqui — e <b>somente</b> aqui — que a pergunta "este usuário pode agir nesta
/// igreja?" é respondida. A claim <c>tenant_id</c> do JWT diz qual tenant o usuário
/// <i>selecionou</i>; a membership diz se ele <i>pode</i>. Confiar na claim sozinha
/// significa aceitar como autorização um dado que só descreve intenção.
/// </para>
/// <para>
/// Pessoa que muda de igreja não vira um novo <c>User</c>: ganha uma segunda
/// membership. A anterior é encerrada com <c>LeftAt</c>, preservando o histórico
/// financeiro e de presença já vinculado àquele tenant.
/// </para>
/// </remarks>
public sealed class Membership : AggregateRoot
{
    private readonly List<MembershipRole> _roles = [];

    private Membership()
    {
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public long TenantId { get; private set; }
    public MembershipStatus Status { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? LeftAt { get; private set; }

    public IReadOnlyCollection<MembershipRole> Roles => _roles.AsReadOnly();

    public static Membership Create(long userId, long tenantId, DateTimeOffset now) => new()
    {
        UserId = userId,
        TenantId = tenantId,
        Status = MembershipStatus.Active,
        JoinedAt = now
    };

    public void GrantRole(long roleId, long? grantedByUserId, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId))
        {
            return;   // idempotente
        }

        _roles.Add(MembershipRole.Create(Id, roleId, grantedByUserId, now));
    }

    public void RevokeRole(long roleId) => _roles.RemoveAll(r => r.RoleId == roleId);

    public void Leave(DateTimeOffset now)
    {
        Status = MembershipStatus.Inactive;
        LeftAt = now;
    }

    public void Revoke(DateTimeOffset now)
    {
        Status = MembershipStatus.Revoked;
        LeftAt = now;
    }

    public bool IsActive() => Status == MembershipStatus.Active;
}

/// <summary>
/// Papel concedido dentro de uma membership.
/// </summary>
/// <remarks>
/// Ancorado em <c>MembershipId</c>, não em <c>UserId</c>: papel só existe dentro de
/// um tenant. Ancorar no usuário permitiria "Tesoureiro" sem igreja — um papel órfão
/// que nenhuma policy conseguiria avaliar.
/// </remarks>
public sealed class MembershipRole
{
    private MembershipRole()
    {
    }

    public long Id { get; private set; }
    public long MembershipId { get; private set; }
    public long RoleId { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public long? GrantedByUserId { get; private set; }

    internal static MembershipRole Create(
        long membershipId,
        long roleId,
        long? grantedByUserId,
        DateTimeOffset now) => new()
        {
            MembershipId = membershipId,
            RoleId = roleId,
            GrantedByUserId = grantedByUserId,
            GrantedAt = now
        };
}

/// <summary>Papel. Códigos de sistema em <see cref="SystemRoles"/>.</summary>
public sealed class Role
{
    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public long Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsSystem { get; private set; }

    /// <summary>NULL para papéis de sistema, disponíveis a todos os tenants.</summary>
    public long? TenantId { get; private set; }

    public static Role CreateSystem(string code, string name) => new()
    {
        Code = code,
        Name = name,
        IsSystem = true,
        TenantId = null
    };
}

public static class SystemRoles
{
    public const string ChurchAdmin = "ChurchAdmin";
    public const string Treasurer = "Treasurer";
    public const string CellLeader = "CellLeader";
    public const string ChildcareStaff = "ChildcareStaff";
    public const string Member = "Member";
}

/// <summary>
/// Permissão atômica. Papéis agrupam permissões; policies combinam permissão com
/// contexto (tenant, posse do recurso, estado).
/// </summary>
public sealed class Permission
{
    private Permission()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public long Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
}

public static class Permissions
{
    public const string MembersRead = "members.read";
    public const string MembersWrite = "members.write";
    public const string GivingRead = "giving.read";
    public const string GivingWrite = "giving.write";
    public const string ChildrenRead = "children.read";
    public const string ChildrenCheckIn = "children.checkin";
    public const string ChildrenCheckout = "children.checkout";
    public const string EventsWrite = "events.write";
    public const string BillingManage = "billing.manage";
}
