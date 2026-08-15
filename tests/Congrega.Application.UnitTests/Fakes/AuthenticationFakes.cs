using System.Reflection;
using System.Text;
using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;

namespace Congrega.Application.UnitTests.Fakes;

/// <summary>
/// Atribui identidades a agregados nos testes.
/// </summary>
/// <remarks>
/// As entidades têm <c>Id</c> com setter privado de propósito — em produção quem o
/// preenche é o banco, via <c>GENERATED ALWAYS AS IDENTITY</c>. Abrir um setter
/// público só para testes permitiria que código de produção também o usasse, que é
/// justamente o que a restrição evita. Reflexão aqui é o preço de manter o domínio
/// honesto, e fica confinada a esta linha.
/// </remarks>
internal static class TestIdentity
{
    public static T WithId<T>(this T entity, long id) where T : notnull
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
        return entity;
    }
}

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _byEmail = new(StringComparer.Ordinal);
    private readonly Dictionary<long, User> _byId = [];

    public List<User> Added { get; } = [];

    public void Seed(User user)
    {
        _byEmail[user.Email] = user;
        _byId[user.Id] = user;
    }

    public Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken _) =>
        Task.FromResult(_byEmail.GetValueOrDefault(normalizedEmail));

    public Task<User?> FindByIdAsync(long userId, CancellationToken _) =>
        Task.FromResult(_byId.GetValueOrDefault(userId));

    public void Add(User user)
    {
        Added.Add(user);
        _byEmail[user.Email] = user;
    }
}

internal sealed class FakeOtpCodeRepository : IEmailVerificationCodeRepository
{
    private readonly List<EmailVerificationCode> _codes = [];

    public List<EmailVerificationCode> Added { get; } = [];
    public int InvalidateCallCount { get; private set; }
    public int IssuedInWindow { get; set; }

    public void Seed(EmailVerificationCode code) => _codes.Add(code);

    public Task<EmailVerificationCode?> FindActiveAsync(
        long userId, OtpPurpose purpose, DateTimeOffset now, CancellationToken _) =>
        Task.FromResult(_codes
            .Where(c => c.UserId == userId && c.Purpose == purpose && c.IsActive(now))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());

    public Task InvalidateActiveAsync(
        long userId, OtpPurpose purpose, DateTimeOffset now, CancellationToken _)
    {
        InvalidateCallCount++;
        foreach (var code in _codes.Where(c => c.UserId == userId && c.IsActive(now)))
        {
            code.Invalidate(now);
        }

        return Task.CompletedTask;
    }

    public Task<int> CountIssuedSinceAsync(
        long userId, OtpPurpose purpose, DateTimeOffset since, CancellationToken _) =>
        Task.FromResult(IssuedInWindow);

    public void Add(EmailVerificationCode code)
    {
        Added.Add(code);
        _codes.Add(code);
    }
}

internal sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly List<RefreshToken> _tokens = [];

    public List<RefreshToken> Added { get; } = [];
    public List<Guid> RevokedFamilies { get; } = [];
    public List<long> RevokedUsers { get; } = [];

    public void Seed(RefreshToken token) => _tokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken _) =>
        Task.FromResult(_tokens.FirstOrDefault(t => t.TokenHash.SequenceEqual(tokenHash)));

    public Task<int> RevokeFamilyAsync(
        Guid familyId, RefreshRevokeReason reason, DateTimeOffset now, CancellationToken _)
    {
        RevokedFamilies.Add(familyId);
        var affected = _tokens.Where(t => t.FamilyId == familyId && t.RevokedAt is null).ToList();
        foreach (var token in affected)
        {
            token.Revoke(reason, now);
        }

        return Task.FromResult(affected.Count);
    }

    public Task<int> RevokeAllForUserAsync(
        long userId, RefreshRevokeReason reason, DateTimeOffset now, CancellationToken _)
    {
        RevokedUsers.Add(userId);
        var affected = _tokens.Where(t => t.UserId == userId && t.RevokedAt is null).ToList();
        foreach (var token in affected)
        {
            token.Revoke(reason, now);
        }

        return Task.FromResult(affected.Count);
    }

    public void Add(RefreshToken token)
    {
        Added.Add(token);
        _tokens.Add(token);
    }
}

internal sealed class FakeMembershipRepository : IMembershipRepository
{
    private readonly List<(TenantSummary Summary, MembershipContext Context)> _memberships = [];

    public void Seed(
        long userId,
        long tenantId,
        Guid tenantPublicId,
        TenantStatus status = TenantStatus.Active,
        string[]? roles = null)
    {
        UserId = userId;
        _memberships.Add((
            new TenantSummary
            {
                TenantId = tenantId,
                PublicId = tenantPublicId,
                Name = $"Igreja {tenantId}",
                Status = status
            },
            new MembershipContext
            {
                MembershipId = tenantId * 100,
                TenantId = tenantId,
                TenantPublicId = tenantPublicId,
                TenantName = $"Igreja {tenantId}",
                TenantStatus = status,
                RoleCodes = roles is { Length: > 0 } ? roles : [SystemRoles.Member],
                PermissionCodes = [Permissions.MembersRead]
            }));
    }

    public long UserId { get; private set; }

    /// <summary>Simula vínculo revogado durante a sessão.</summary>
    public void RevokeAll() => _memberships.Clear();

    public Task<MembershipContext?> FindActiveAsync(long userId, long tenantId, CancellationToken _) =>
        Task.FromResult(_memberships
            .Where(m => m.Context.TenantId == tenantId)
            .Select(m => m.Context)
            .FirstOrDefault());

    public Task<IReadOnlyList<TenantSummary>> ListActiveTenantsAsync(long userId, CancellationToken _) =>
        Task.FromResult<IReadOnlyList<TenantSummary>>(_memberships.Select(m => m.Summary).ToList());
}

/// <summary>
/// Hasher determinístico. Conta chamadas — é o que permite provar que o caminho
/// "usuário inexistente" também gasta um hash, e portanto não vaza por latência.
/// </summary>
internal sealed class FakeSecretHasher : ISecretHasher
{
    public int HashOtpCallCount { get; private set; }

    public byte[] HashOtp(string code)
    {
        HashOtpCallCount++;
        return Encoding.UTF8.GetBytes($"otp:{code}");
    }

    public byte[] HashToken(string tokenValue) => Encoding.UTF8.GetBytes($"tok:{tokenValue}");

    public bool FixedTimeEquals(byte[] left, byte[] right) => left.SequenceEqual(right);
}

internal sealed class FakeOtpGenerator(string code = "123456") : IOtpGenerator
{
    public string Generate() => code;
}

internal sealed class FakeTokenIssuer : ITokenIssuer
{
    private int _counter;

    public List<AccessTokenRequest> Issued { get; } = [];

    public IssuedAccessToken IssueAccessToken(AccessTokenRequest request)
    {
        Issued.Add(request);
        return new IssuedAccessToken
        {
            Value = $"access-token-{++_counter}",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            JwtId = Guid.NewGuid().ToString("N")
        };
    }

    public string GenerateRefreshTokenValue() => $"refresh-{Guid.NewGuid():N}";
}

internal sealed class FakeTierProvider(string? tier = null) : ISubscriptionTierProvider
{
    public Task<string?> GetActiveTierAsync(long userId, CancellationToken _) => Task.FromResult(tier);
}

internal sealed class FakeOutbox : IOutbox
{
    public List<(string MessageType, object Payload)> Messages { get; } = [];

    public void Enqueue(string messageType, object payload, string? correlationId = null) =>
        Messages.Add((messageType, payload));

    public bool Contains(string messageType) =>
        Messages.Any(m => string.Equals(m.MessageType, messageType, StringComparison.Ordinal));

    /// <summary>Procura um evento de segurança pelo campo <c>eventType</c> do payload anônimo.</summary>
    public bool ContainsSecurityEvent(string eventType) =>
        Messages
            .Where(m => m.MessageType == "SecurityEvent")
            .Any(m => m.Payload.GetType().GetProperty("eventType")?.GetValue(m.Payload) as string == eventType);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCallCount++;
        return Task.FromResult(1);
    }
}
