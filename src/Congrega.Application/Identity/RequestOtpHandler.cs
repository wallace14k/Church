using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Identity;

public sealed record RequestOtpCommand
{
    public required string Email { get; init; }
    public string? FullName { get; init; }
    public string? RequestIp { get; init; }
}

/// <summary>
/// Resultado da solicitação de OTP.
/// </summary>
/// <remarks>
/// Não existe variante de falha por "usuário não encontrado", e isso é intencional:
/// a API responde <c>202</c> em todos os casos. As propriedades abaixo servem só
/// para métrica e log interno — nunca para compor a resposta HTTP.
/// </remarks>
public sealed record RequestOtpResult
{
    public required bool CodeIssued { get; init; }
    public required bool UserCreated { get; init; }
}

/// <summary>
/// Cadastro e login são o mesmo fluxo: solicitar um código para um e-mail
/// desconhecido cria a conta. Não há tela separada de "criar conta".
/// </summary>
public sealed class RequestOtpHandler(
    IUserRepository users,
    IEmailVerificationCodeRepository codes,
    IOtpGenerator otpGenerator,
    ISecretHasher hasher,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RequestOtpHandler> logger)
{
    /// <summary>Janela do rate limiting por e-mail.</summary>
    private static readonly TimeSpan EmailRateWindow = TimeSpan.FromMinutes(15);

    /// <summary>Máximo de códigos emitidos por e-mail dentro da janela.</summary>
    private const int MaxCodesPerWindow = 5;

    public async Task<RequestOtpResult> HandleAsync(
        RequestOtpCommand command,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedEmail = User.NormalizeEmail(command.Email);

        var user = await users.FindByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        bool userCreated = false;

        if (user is null)
        {
            user = User.Register(normalizedEmail, command.FullName ?? DeriveNameFromEmail(normalizedEmail), now);
            users.Add(user);
            userCreated = true;
        }

        if (!user.CanAuthenticate())
        {
            // Conta bloqueada ou anonimizada: nada é emitido, mas o chamador recebe
            // a MESMA resposta de sucesso. Sinalizar o bloqueio aqui entregaria ao
            // atacante a informação de que o e-mail existe e está sob restrição.
            logger.LogWarning(
                "Solicitação de OTP para conta não autenticável {UserId} (situação {Status}).",
                user.Id, user.Status);

            return new RequestOtpResult { CodeIssued = false, UserCreated = false };
        }

        // Rate limiting por e-mail, contado NO BANCO.
        //
        // O limitador de borda do ASP.NET Core particiona por IP, o que não cobre o
        // ataque que importa aqui: solicitar códigos repetidamente para o e-mail de
        // uma vítima, a partir de IPs diferentes, para inundar a caixa dela ou forçar
        // rotação de códigos. Contar emissões por usuário fecha esse vetor.
        //
        // A contagem vem do banco, e não de IMemoryCache, porque o limite precisa ser
        // global: com três réplicas, um contador em memória transformaria o limite de
        // 5 em 15 na prática.
        if (user.Id != 0)
        {
            int recentCodes = await codes.CountIssuedSinceAsync(
                user.Id, OtpPurpose.Login, now - EmailRateWindow, cancellationToken);

            if (recentCodes >= MaxCodesPerWindow)
            {
                logger.LogWarning(
                    "Rate limit por e-mail atingido para usuário {UserId}: {Count} códigos em {Window}.",
                    user.Id, recentCodes, EmailRateWindow);

                // Mesma resposta de sucesso. Sinalizar o bloqueio confirmaria ao
                // atacante que o e-mail existe e está sendo alvo.
                return new RequestOtpResult { CodeIssued = false, UserCreated = false };
            }
        }

        // Um código válido por vez. Sem isso, cada reenvio ampliaria o espaço de
        // busca do atacante — cinco códigos ativos são cinco chances simultâneas —
        // em vez de apenas renovar o código anterior.
        await codes.InvalidateActiveAsync(user.Id, OtpPurpose.Login, now, cancellationToken);

        string plainCode = otpGenerator.Generate();

        var verificationCode = EmailVerificationCode.Issue(
            userId: user.Id,
            codeHash: hasher.HashOtp(plainCode),
            purpose: OtpPurpose.Login,
            now: now,
            requestIp: command.RequestIp);

        codes.Add(verificationCode);

        // O e-mail sai pelo Outbox, na mesma transação. Enviar inline abriria a
        // janela clássica: código gravado e e-mail não enviado (usuário travado), ou
        // e-mail enviado e transação revertida (código que não existe).
        //
        // Este é o único ponto do sistema em que o código em texto plano trafega, e
        // ele vai para o payload do Outbox — que é lido apenas pelo dispatcher e
        // nunca aparece em log. Ver docs/02-autenticacao.md.
        outbox.Enqueue(
            "SendOtpEmail",
            new
            {
                userId = user.Id,
                email = user.Email,
                fullName = user.FullName,
                code = plainCode,
                expiresAt = verificationCode.ExpiresAt
            });

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "OTP emitido para usuário {UserId}. Conta criada nesta solicitação: {UserCreated}.",
            user.Id, userCreated);

        return new RequestOtpResult { CodeIssued = true, UserCreated = userCreated };
    }

    /// <summary>
    /// Nome provisório a partir do e-mail, para a conta criada no primeiro login.
    /// O usuário corrige no onboarding; pedir o nome antes do código adicionaria
    /// atrito no ponto de maior abandono do funil.
    /// </summary>
    private static string DeriveNameFromEmail(string normalizedEmail)
    {
        var localPart = normalizedEmail.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "Novo usuário" : localPart;
    }
}
