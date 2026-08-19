using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Congrega.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Identity;

public sealed record VerifyOtpCommand
{
    public required string Email { get; init; }
    public required string Code { get; init; }

    /// <summary>Tenant a selecionar. Se ausente, ver a regra de seleção automática no handler.</summary>
    public Guid? TenantPublicId { get; init; }

    public string? DeviceLabel { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>Sessão emitida após validação bem-sucedida.</summary>
public sealed record AuthenticatedSession
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAt { get; init; }

    /// <summary>
    /// Valor em texto plano do refresh token. Existe apenas nesta resposta — o banco
    /// guarda somente o hash. Nunca deve ser logado.
    /// </summary>
    public required string RefreshToken { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }
    public required Guid UserPublicId { get; init; }

    /// <summary>Nome do titular, para a interface saudar e identificar a conta.</summary>
    public required string FullName { get; init; }
    public Guid? TenantPublicId { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed record VerifyOtpResult
{
    public AuthenticatedSession? Session { get; init; }

    /// <summary>Motivo interno da falha. Para log e métrica — <b>nunca</b> para a resposta HTTP.</summary>
    public string? FailureReason { get; init; }

    public bool Succeeded => Session is not null;

    public static VerifyOtpResult Failure(string reason) => new() { FailureReason = reason };
    public static VerifyOtpResult Success(AuthenticatedSession session) => new() { Session = session };
}

/// <summary>
/// Valida o código e emite a sessão.
/// </summary>
/// <remarks>
/// <para>
/// <b>Toda falha devolve o mesmo <see cref="VerifyOtpResult"/> genérico.</b> Usuário
/// inexistente, código expirado, código errado e tentativas esgotadas são
/// indistinguíveis para o chamador. Distingui-los transformaria o endpoint em um
/// oráculo: um atacante saberia quais e-mails existem e quando um código ainda está
/// ativo, o que é metade do trabalho de um ataque direcionado.
/// </para>
/// <para>
/// <b>Bypass pelo frontend é impossível por construção:</b> o cliente envia dois
/// campos e recebe tokens ou erro. Não existe resposta parcial, flag de "código
/// correto" nem estado intermediário que um cliente modificado pudesse forçar.
/// </para>
/// </remarks>
public sealed class VerifyOtpHandler(
    IUserRepository users,
    IEmailVerificationCodeRepository codes,
    IRefreshTokenRepository refreshTokens,
    IMembershipRepository memberships,
    ISecretHasher hasher,
    ITokenIssuer tokenIssuer,
    ISubscriptionTierProvider tierProvider,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAuthenticationContextWriter authContext,
    ILogger<VerifyOtpHandler> logger)
{
    public async Task<VerifyOtpResult> HandleAsync(
        VerifyOtpCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedEmail = User.NormalizeEmail(command.Email);

        var user = await users.FindByNormalizedEmailAsync(normalizedEmail, cancellationToken);

        if (user is null || !user.CanAuthenticate())
        {
            // Hash descartado de propósito. Sem ele, o caminho "usuário inexistente"
            // retornaria mensurável e consistentemente mais rápido que o caminho
            // real, e a diferença de latência viraria o oráculo que a resposta
            // uniforme tenta evitar.
            _ = hasher.HashOtp(command.Code);
            return VerifyOtpResult.Failure("user_not_found_or_blocked");
        }

        var code = await codes.FindActiveAsync(user.Id, OtpPurpose.Login, now, cancellationToken);

        if (code is null)
        {
            _ = hasher.HashOtp(command.Code);
            return VerifyOtpResult.Failure("no_active_code");
        }

        var candidateHash = hasher.HashOtp(command.Code);
        var validation = code.Validate(candidateHash, hasher.FixedTimeEquals, now);

        if (validation != OtpValidationResult.Valid)
        {
            // CRÍTICO: persistir mesmo em caso de falha. Validate() incrementou
            // AttemptCount, e sem este SaveChanges o contador voltaria a zero a cada
            // tentativa — o limite de 5 nunca seria alcançado e a força bruta sobre
            // 10⁶ combinações ficaria viável.
            if (validation is OtpValidationResult.Mismatch or OtpValidationResult.TooManyAttempts)
            {
                outbox.Enqueue("SecurityEvent", new
                {
                    eventType = validation == OtpValidationResult.TooManyAttempts
                        ? "OtpMaxAttempts"
                        : "OtpMismatch",
                    userId = user.Id,
                    severity = validation == OtpValidationResult.TooManyAttempts ? 2 : 1,
                    occurredAt = now
                });
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Validação de OTP falhou para usuário {UserId}: {Reason}. Tentativas: {Attempts}.",
                user.Id, validation, code.AttemptCount);

            return VerifyOtpResult.Failure(validation.ToString());
        }

        user.MarkEmailVerified(now);
        user.RecordLogin(now);

        // Identidade confirmada pelo OTP, não por claim: este endpoint é anônimo,
        // então o middleware nunca populou o contexto. Sem isto, o interceptor de
        // conexão manda app.user_id vazio, e ListActiveTenantsAsync — que resolve
        // o tenant abaixo — voltaria vazia mesmo para quem tem vínculo ativo.
        authContext.SetAuthenticatedUser(user.Id);

        var membership = await ResolveMembershipAsync(user.Id, command.TenantPublicId, cancellationToken);
        var tier = await tierProvider.GetActiveTierAsync(user.Id, cancellationToken);

        var accessToken = tokenIssuer.IssueAccessToken(new AccessTokenRequest
        {
            UserId = user.Id,
            UserPublicId = user.PublicId,
            Email = user.Email,
            EmailVerified = true,
            TenantId = membership?.TenantId,
            Roles = membership?.RoleCodes ?? [],
            Permissions = membership?.PermissionCodes ?? [],
            SubscriptionTier = tier
        });

        string refreshValue = tokenIssuer.GenerateRefreshTokenValue();
        var refreshToken = RefreshToken.StartFamily(
            userId: user.Id,
            tokenHash: hasher.HashToken(refreshValue),
            now: now,
            selectedTenantId: membership?.TenantId,
            deviceLabel: command.DeviceLabel,
            ipAddress: command.IpAddress);

        refreshTokens.Add(refreshToken);

        outbox.Enqueue("SecurityEvent", new
        {
            eventType = "LoginSucceeded",
            userId = user.Id,
            severity = 1,
            occurredAt = now
        });

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Login concluído para usuário {UserId}, tenant {TenantId}.",
            user.Id, membership?.TenantId);

        return VerifyOtpResult.Success(new AuthenticatedSession
        {
            AccessToken = accessToken.Value,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshValue,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            UserPublicId = user.PublicId,
            FullName = user.FullName,
            TenantPublicId = membership?.TenantPublicId,
            Roles = membership?.RoleCodes ?? []
        });
    }

    /// <summary>
    /// Resolve o tenant da sessão.
    /// </summary>
    /// <remarks>
    /// <para>Três casos, nesta ordem:</para>
    /// <list type="number">
    ///   <item><description>
    ///     Tenant pedido explicitamente → valida a membership. Sem vínculo ativo, a
    ///     sessão sai <b>sem</b> tenant, e não com o tenant pedido — a claim descreve
    ///     escolha, o banco decide permissão.
    ///   </description></item>
    ///   <item><description>
    ///     Nenhum tenant pedido e exatamente um vínculo ativo → seleciona
    ///     automaticamente. É o caso da esmagadora maioria dos usuários, e poupar
    ///     uma tela de seleção com uma opção só é ganho real de usabilidade.
    ///   </description></item>
    ///   <item><description>
    ///     Nenhum vínculo, ou vários → sessão sem tenant. O assinante Congrega+ sem
    ///     igreja cai aqui e é cidadão de primeira classe: acessa todo o conteúdo
    ///     premium e nada do ChMS, sem nenhum tratamento especial.
    ///   </description></item>
    /// </list>
    /// </remarks>
    private async Task<MembershipContext?> ResolveMembershipAsync(
        long userId,
        Guid? requestedTenantPublicId,
        CancellationToken cancellationToken)
    {
        var activeTenants = await memberships.ListActiveTenantsAsync(userId, cancellationToken);

        var target = requestedTenantPublicId is { } requested
            ? activeTenants.FirstOrDefault(t => t.PublicId == requested)
            : activeTenants.Count == 1 ? activeTenants[0] : null;

        if (target is null)
        {
            if (requestedTenantPublicId is not null)
            {
                logger.LogWarning(
                    "Usuário {UserId} pediu tenant {TenantPublicId} sem vínculo ativo. "
                    + "Sessão emitida sem tenant.",
                    userId, requestedTenantPublicId);
            }

            return null;
        }

        // Tenant suspenso por inadimplência não autentica. A verificação precisa
        // acontecer no login, e não só na tela de cobrança — senão a sessão emitida
        // antes da suspensão continuaria valendo.
        if (!IsTenantAccessible(target.Status))
        {
            logger.LogWarning(
                "Tenant {TenantId} está {Status}; sessão emitida sem tenant.",
                target.TenantId, target.Status);
            return null;
        }

        return await memberships.FindActiveAsync(userId, target.TenantId, cancellationToken);
    }

    private static bool IsTenantAccessible(TenantStatus status) =>
        status is TenantStatus.Trial or TenantStatus.Active;
}
