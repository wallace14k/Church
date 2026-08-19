using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Identity;

public sealed record RefreshSessionCommand
{
    public required string RefreshToken { get; init; }

    /// <summary>Troca de igreja no mesmo refresh. Se ausente, mantém o tenant da sessão.</summary>
    public Guid? SwitchToTenantPublicId { get; init; }

    public string? IpAddress { get; init; }
}

public sealed record RefreshSessionResult
{
    public AuthenticatedSession? Session { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// Indica que a family inteira foi revogada por suspeita de roubo. A API usa isso
    /// para instruir o cliente a limpar o storage e voltar à tela de login — sem
    /// revelar ao chamador <i>por que</i> a sessão caiu.
    /// </summary>
    public bool SessionTerminated { get; init; }

    public bool Succeeded => Session is not null;
}

/// <summary>
/// Rotaciona o refresh token e reemite o access token.
/// </summary>
/// <remarks>
/// <para>
/// É aqui que a revogação de verdade acontece. O access token vive 15 minutos e não
/// é revogável; a segurança da sessão longa está inteiramente neste fluxo, que
/// revalida usuário, membership e papéis <b>a cada rotação</b>. Um usuário que perdeu
/// o papel de tesoureiro há dez minutos perde o acesso na próxima rotação, sem
/// necessidade de lista negra de tokens.
/// </para>
/// </remarks>
public sealed class RefreshSessionHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IMembershipRepository memberships,
    ISecretHasher hasher,
    ITokenIssuer tokenIssuer,
    ISubscriptionTierProvider tierProvider,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAuthenticationContextWriter authContext,
    ILogger<RefreshSessionHandler> logger)
{
    public async Task<RefreshSessionResult> HandleAsync(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var presentedHash = hasher.HashToken(command.RefreshToken);

        var token = await refreshTokens.FindByHashAsync(presentedHash, cancellationToken);

        if (token is null)
        {
            return new RefreshSessionResult { FailureReason = "unknown_token" };
        }

        var outcome = token.Evaluate(now);

        if (outcome == RefreshTokenOutcome.ReuseDetected)
        {
            return await HandleReuseAsync(token, now, cancellationToken);
        }

        if (outcome != RefreshTokenOutcome.Rotate)
        {
            logger.LogInformation(
                "Refresh recusado para usuário {UserId}: {Outcome}.", token.UserId, outcome);
            return new RefreshSessionResult { FailureReason = outcome.ToString() };
        }

        var user = await users.FindByIdAsync(token.UserId, cancellationToken);

        if (user is null || !user.CanAuthenticate())
        {
            // A conta foi bloqueada ou anonimizada depois que a sessão começou.
            // Derruba tudo — um token válido não pode sobreviver ao bloqueio da conta.
            await refreshTokens.RevokeAllForUserAsync(
                token.UserId, RefreshRevokeReason.AdminRevoked, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new RefreshSessionResult
            {
                FailureReason = "user_not_authenticatable",
                SessionTerminated = true
            };
        }

        // Identidade confirmada pelo hash do refresh token, não por claim: este
        // endpoint é anônimo, então o middleware nunca populou o contexto. Sem
        // isto, a busca de tenants abaixo e a revalidação de membership mandariam
        // app.user_id vazio ao Postgres e voltariam vazias por RLS.
        authContext.SetAuthenticatedUser(user.Id);

        // Troca de igreja aproveitando a rotação: mantém a family e, com ela, a
        // detecção de reuso. Emitir uma family nova a cada troca de contexto
        // fragmentaria o rastreamento e criaria sessões órfãs.
        if (command.SwitchToTenantPublicId is { } switchTo)
        {
            var available = await memberships.ListActiveTenantsAsync(user.Id, cancellationToken);
            var target = available.FirstOrDefault(t => t.PublicId == switchTo);

            if (target is null)
            {
                logger.LogWarning(
                    "Usuário {UserId} tentou trocar para o tenant {TenantPublicId} sem vínculo ativo.",
                    user.Id, switchTo);
                return new RefreshSessionResult { FailureReason = "tenant_not_available" };
            }

            token.SelectTenant(target.TenantId);
        }

        // Revalidação a cada rotação: papéis e permissões são relidos do banco, não
        // copiados do token anterior. É o que faz uma revogação de papel valer em no
        // máximo 15 minutos, sem infraestrutura adicional.
        var membership = token.SelectedTenantId is { } tenantId
            ? await memberships.FindActiveAsync(user.Id, tenantId, cancellationToken)
            : null;

        if (token.SelectedTenantId is not null && membership is null)
        {
            // Vínculo revogado durante a sessão: a sessão continua, mas sem tenant.
            // Derrubar tudo seria hostil com quem apenas saiu de uma igreja e ainda
            // é assinante Congrega+.
            logger.LogInformation(
                "Vínculo do usuário {UserId} com o tenant {TenantId} não está mais ativo. "
                + "Sessão segue sem tenant.",
                user.Id, token.SelectedTenantId);
            token.SelectTenant(null);
        }

        var tier = await tierProvider.GetActiveTierAsync(user.Id, cancellationToken);

        var accessToken = tokenIssuer.IssueAccessToken(new AccessTokenRequest
        {
            UserId = user.Id,
            UserPublicId = user.PublicId,
            Email = user.Email,
            EmailVerified = user.EmailVerified,
            TenantId = membership?.TenantId,
            Roles = membership?.RoleCodes ?? [],
            Permissions = membership?.PermissionCodes ?? [],
            SubscriptionTier = tier
        });

        string newRefreshValue = tokenIssuer.GenerateRefreshTokenValue();

        var rotated = token.Rotate(
            newTokenHash: hasher.HashToken(newRefreshValue),
            now: now,
            ipAddress: command.IpAddress);

        refreshTokens.Add(rotated);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RefreshSessionResult
        {
            Session = new AuthenticatedSession
            {
                AccessToken = accessToken.Value,
                AccessTokenExpiresAt = accessToken.ExpiresAt,
                RefreshToken = newRefreshValue,
                RefreshTokenExpiresAt = rotated.ExpiresAt,
                UserPublicId = user.PublicId,
                FullName = user.FullName,
                TenantPublicId = membership?.TenantPublicId,
                Roles = membership?.RoleCodes ?? []
            }
        };
    }

    /// <summary>
    /// Um token já rotacionado foi apresentado de novo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duas explicações possíveis: o atacante roubou o token e está usando, ou o
    /// cliente legítimo repetiu a requisição por falha de rede depois que o atacante
    /// já rodou. <b>Não há como distinguir</b> — o servidor vê exatamente a mesma
    /// coisa nos dois casos.
    /// </para>
    /// <para>
    /// Diante do empate, a escolha é conservadora: revoga a family inteira. O custo
    /// para o usuário legítimo é um login; o custo de errar para o outro lado é a
    /// conta comprometida por até 30 dias.
    /// </para>
    /// </remarks>
    private async Task<RefreshSessionResult> HandleReuseAsync(
        RefreshToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int revoked = await refreshTokens.RevokeFamilyAsync(
            token.FamilyId, RefreshRevokeReason.ReuseDetected, now, cancellationToken);

        token.RaiseReuseDetected(now);

        outbox.Enqueue("SecurityEvent", new
        {
            eventType = "RefreshTokenReuseDetected",
            userId = token.UserId,
            familyId = token.FamilyId,
            severity = 3,          // crítico: dispara alerta imediato
            revokedTokens = revoked,
            occurredAt = now
        });

        // Avisar o titular é parte do controle, não cortesia: se foi roubo, ele é a
        // única pessoa capaz de reconhecer que não foi ele e agir.
        outbox.Enqueue("SendSecurityAlertEmail", new
        {
            userId = token.UserId,
            template = "security.session_terminated",
            occurredAt = now
        });

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Reuso de refresh token detectado para usuário {UserId}. "
            + "Family {FamilyId} revogada ({Revoked} tokens).",
            token.UserId, token.FamilyId, revoked);

        return new RefreshSessionResult
        {
            FailureReason = "reuse_detected",
            SessionTerminated = true
        };
    }
}
