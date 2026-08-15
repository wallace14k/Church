using Congrega.Domain.Tenancy;

namespace Congrega.Domain.Identity;

/// <summary>
/// Repositórios do contexto de identidade.
/// </summary>
/// <remarks>
/// Cada método existe porque um caso de uso precisa exatamente daquela pergunta.
/// Nenhum expõe <c>IQueryable</c> nem recebe predicado — isso vazaria a composição
/// de query para a camada de aplicação e traria EF Core junto, que é o anti-padrão
/// do <c>IRepository&lt;T&gt;</c> genérico vedado pelo briefing.
/// </remarks>
public interface IUserRepository
{
    /// <summary>Busca por e-mail já normalizado (<see cref="User.NormalizeEmail"/>).</summary>
    Task<User?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(long userId, CancellationToken cancellationToken);

    void Add(User user);
}

public interface IEmailVerificationCodeRepository
{
    /// <summary>Código ativo mais recente do usuário para a finalidade indicada.</summary>
    Task<EmailVerificationCode?> FindActiveAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invalida todos os códigos ativos do usuário. Chamado antes de emitir um novo,
    /// garantindo <b>um código válido por vez</b> — sem isso, cada reenvio ampliaria
    /// o espaço de busca do atacante em vez de apenas renová-lo.
    /// </summary>
    Task InvalidateActiveAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quantos códigos foram emitidos para o usuário desde o instante informado.
    /// </summary>
    /// <remarks>
    /// Base do rate limiting por e-mail. Contar no <b>banco</b>, e não em memória, é
    /// o que torna o limite real com várias réplicas: um contador em
    /// <c>IMemoryCache</c> seria por pod, e com três réplicas o limite de 5 viraria
    /// 15 na prática. A tabela já registra cada emissão — o controle sai de graça.
    /// </remarks>
    Task<int> CountIssuedSinceAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    void Add(EmailVerificationCode code);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Revoga toda a family de uma vez, em uma única instrução.
    /// </summary>
    /// <remarks>
    /// Set-based de propósito: carregar a family e revogar em laço seria N+1 no pior
    /// momento possível — o de responder a um indício de conta comprometida, quando
    /// a janela de exposição é contada em segundos.
    /// </remarks>
    Task<int> RevokeFamilyAsync(
        Guid familyId,
        RefreshRevokeReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> RevokeAllForUserAsync(
        long userId,
        RefreshRevokeReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void Add(RefreshToken token);
}

public interface IMembershipRepository
{
    /// <summary>
    /// Membership ativa do usuário no tenant, com papéis e permissões resolvidos.
    /// </summary>
    /// <remarks>
    /// Devolve <c>null</c> quando não há vínculo ativo — o que faz a autenticação
    /// recusar o <c>tenant_id</c> pedido, mesmo que a claim do token o traga.
    /// </remarks>
    Task<MembershipContext?> FindActiveAsync(
        long userId,
        long tenantId,
        CancellationToken cancellationToken);

    /// <summary>Tenants em que o usuário tem vínculo ativo. Usado na seleção de igreja.</summary>
    Task<IReadOnlyList<TenantSummary>> ListActiveTenantsAsync(
        long userId,
        CancellationToken cancellationToken);
}

/// <summary>Membership resolvida com papéis e permissões, pronta para virar claims.</summary>
public sealed record MembershipContext
{
    public required long MembershipId { get; init; }
    public required long TenantId { get; init; }
    public required Guid TenantPublicId { get; init; }
    public required string TenantName { get; init; }
    public required TenantStatus TenantStatus { get; init; }
    public required IReadOnlyList<string> RoleCodes { get; init; }
    public required IReadOnlyList<string> PermissionCodes { get; init; }
}

public sealed record TenantSummary
{
    public required long TenantId { get; init; }
    public required Guid PublicId { get; init; }
    public required string Name { get; init; }
    public required TenantStatus Status { get; init; }
}
