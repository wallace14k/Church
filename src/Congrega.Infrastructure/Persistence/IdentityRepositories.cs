using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Congrega.Infrastructure.Persistence;

internal sealed class UserRepository(CongregaDbContext db) : IUserRepository
{
    public Task<User?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken) =>
        // Com tracking: o chamador altera o usuário (MarkEmailVerified, RecordLogin)
        // e espera que SaveChanges persista. AsNoTracking aqui produziria uma falha
        // silenciosa — nada quebra, nada é salvo.
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<User?> FindByIdAsync(long userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class EmailVerificationCodeRepository(CongregaDbContext db)
    : IEmailVerificationCodeRepository
{
    public Task<EmailVerificationCode?> FindActiveAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.EmailVerificationCodes
            .Where(c => c.UserId == userId
                        && c.Purpose == purpose
                        && c.ConsumedAt == null
                        && c.ExpiresAt > now)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Invalida em uma única instrução.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ExecuteUpdateAsync</c> em vez de carregar e alterar em laço: normalmente há
    /// um código ativo, mas nada garante isso sob reenvio concorrente, e a versão
    /// set-based custa o mesmo para uma linha ou para vinte.
    /// </para>
    /// <para>
    /// <b>Atenção ao ciclo de vida:</b> <c>ExecuteUpdate</c> ignora o change tracker
    /// e vai ao banco na hora, fora do <c>SaveChanges</c>. Aqui isso é aceitável e até
    /// desejável — invalidar códigos antigos antes de emitir o novo é seguro mesmo se
    /// a transação seguinte falhar; o pior resultado é o usuário pedir outro código.
    /// </para>
    /// </remarks>
    public Task InvalidateActiveAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.EmailVerificationCodes
            .Where(c => c.UserId == userId
                        && c.Purpose == purpose
                        && c.ConsumedAt == null
                        && c.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ExpiresAt, now),
                cancellationToken);

    public Task<int> CountIssuedSinceAsync(
        long userId,
        OtpPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        db.EmailVerificationCodes
            .CountAsync(
                c => c.UserId == userId && c.Purpose == purpose && c.CreatedAt >= since,
                cancellationToken);

    public void Add(EmailVerificationCode code) => db.EmailVerificationCodes.Add(code);
}

internal sealed class RefreshTokenRepository(CongregaDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    /// <summary>
    /// Revoga a family inteira em uma instrução.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set-based porque este é o pior momento possível para um N+1: responder a um
    /// indício de conta comprometida, com a janela de exposição contada em segundos.
    /// Uma sessão de 30 dias pode ter milhares de tokens na cadeia.
    /// </para>
    /// <para>
    /// A execução imediata, fora do <c>SaveChanges</c>, é <b>deliberada</b> aqui: a
    /// revogação vale mesmo se a gravação do evento de segurança falhar em seguida.
    /// Entre "revogar e talvez não alertar" e "alertar e talvez não revogar", a
    /// primeira é a única ordem aceitável.
    /// </para>
    /// </remarks>
    public Task<int> RevokeFamilyAsync(
        Guid familyId,
        RefreshRevokeReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now)
                      .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);

    public Task<int> RevokeAllForUserAsync(
        long userId,
        RefreshRevokeReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now)
                      .SetProperty(t => t.RevokedReason, reason),
                cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}

internal sealed class MembershipRepository(CongregaDbContext db) : IMembershipRepository
{
    /// <summary>
    /// Resolve vínculo, papéis e permissões em <b>uma</b> ida ao banco.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Roda a cada login e a cada rotação de refresh — é caminho quente. A versão
    /// ingênua (carregar a membership, depois os papéis, depois as permissões de cada
    /// papel) seria um N+1 de manual: três papéis viram cinco queries.
    /// </para>
    /// <para>
    /// <c>IgnoreQueryFilters</c> é necessário aqui e merece justificativa: esta
    /// consulta é o que <i>estabelece</i> o contexto de tenant. Aplicar o filtro
    /// global antes de o contexto existir devolveria vazio sempre — a dependência
    /// seria circular. O isolamento é garantido pelo predicado explícito
    /// <c>m.UserId == userId</c>, que restringe ao próprio usuário autenticado.
    /// </para>
    /// </remarks>
    public async Task<MembershipContext?> FindActiveAsync(
        long userId,
        long tenantId,
        CancellationToken cancellationToken)
    {
        var result = await db.Memberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == userId
                        && m.TenantId == tenantId
                        && m.Status == MembershipStatus.Active)
            .Select(m => new
            {
                m.Id,
                m.TenantId,
                Tenant = db.Tenants
                    .Where(t => t.Id == m.TenantId)
                    .Select(t => new { t.PublicId, t.Name, t.Status })
                    .First(),
                RoleCodes = db.MembershipRoles
                    .Where(mr => mr.MembershipId == m.Id)
                    .Join(db.Roles, mr => mr.RoleId, r => r.Id, (_, r) => r.Code)
                    .ToList(),
                PermissionCodes = db.MembershipRoles
                    .Where(mr => mr.MembershipId == m.Id)
                    .Join(db.Set<RolePermission>(), mr => mr.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
                    .Join(db.Permissions, pid => pid, p => p.Id, (_, p) => p.Code)
                    .Distinct()
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        return new MembershipContext
        {
            MembershipId = result.Id,
            TenantId = result.TenantId,
            TenantPublicId = result.Tenant.PublicId,
            TenantName = result.Tenant.Name,
            TenantStatus = result.Tenant.Status,
            RoleCodes = result.RoleCodes,
            PermissionCodes = result.PermissionCodes
        };
    }

    public async Task<IReadOnlyList<TenantSummary>> ListActiveTenantsAsync(
        long userId,
        CancellationToken cancellationToken) =>
        await db.Memberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            .Join(db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new TenantSummary
            {
                TenantId = t.Id,
                PublicId = t.PublicId,
                Name = t.Name,
                Status = t.Status
            })
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
}
