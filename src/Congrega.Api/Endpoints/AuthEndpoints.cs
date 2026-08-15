using System.ComponentModel.DataAnnotations;
using Congrega.Application.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record RequestOtpRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public required string Email { get; init; }

    [MaxLength(200)]
    public string? FullName { get; init; }
}

public sealed record VerifyOtpRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public required string Email { get; init; }

    [Required, StringLength(6, MinimumLength = 6)]
    public required string Code { get; init; }

    public Guid? TenantId { get; init; }

    [MaxLength(120)]
    public string? DeviceLabel { get; init; }
}

public sealed record RefreshRequest
{
    public string? RefreshToken { get; init; }
    public Guid? SwitchToTenantId { get; init; }
}

/// <summary>
/// Resposta de sessão.
/// </summary>
/// <remarks>
/// DTO próprio, nunca a entidade de domínio — expor <c>User</c> faria qualquer campo
/// novo do domínio vazar para o contrato público sem ninguém decidir por isso.
/// No web, <c>RefreshToken</c> vem <c>null</c>: o valor viaja em cookie
/// <c>HttpOnly</c>, fora do alcance de JavaScript.
/// </remarks>
public sealed record SessionResponse
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? RefreshToken { get; init; }
    public required Guid UserId { get; init; }
    public Guid? TenantId { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public static class AuthEndpoints
{
    private const string RefreshCookieName = "congrega_rt";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Autenticação")
            .RequireRateLimiting("auth");

        group.MapPost("/otp/request", RequestOtpAsync)
            .WithSummary("Solicita um código de acesso por e-mail")
            .AllowAnonymous();

        group.MapPost("/otp/verify", VerifyOtpAsync)
            .WithSummary("Valida o código e emite a sessão")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithSummary("Rotaciona o refresh token e reemite o access token")
            .AllowAnonymous();
    }

    /// <summary>
    /// Sempre <c>202 Accepted</c>.
    /// </summary>
    /// <remarks>
    /// Independente de o e-mail existir, estar bloqueado ou ter estourado o rate
    /// limit. Qualquer diferenciação aqui transformaria o endpoint em um oráculo de
    /// enumeração de usuários.
    /// </remarks>
    private static async Task<IResult> RequestOtpAsync(
        [FromBody] RequestOtpRequest request,
        RequestOtpHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(
            new RequestOtpCommand
            {
                Email = request.Email,
                FullName = request.FullName,
                RequestIp = httpContext.Connection.RemoteIpAddress?.ToString()
            },
            cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> VerifyOtpAsync(
        [FromBody] VerifyOtpRequest request,
        VerifyOtpHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new VerifyOtpCommand
            {
                Email = request.Email,
                Code = request.Code,
                TenantPublicId = request.TenantId,
                DeviceLabel = request.DeviceLabel,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString()
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            // Mensagem única para todas as falhas. O motivo real (código expirado,
            // errado, tentativas esgotadas, usuário inexistente) fica só no log.
            return TypedResults.Problem(
                title: "Código inválido",
                detail: "O código informado é inválido ou expirou. Solicite um novo código.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return BuildSessionResult(result.Session!, httpContext);
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest? request,
        RefreshSessionHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Cookie primeiro, corpo depois. No web o token está no cookie HttpOnly e o
        // JavaScript não consegue lê-lo para enviar no corpo — que é exatamente o
        // ponto. No mobile, onde não há cookie, vem no corpo.
        string? presented = httpContext.Request.Cookies[RefreshCookieName] ?? request?.RefreshToken;

        if (string.IsNullOrWhiteSpace(presented))
        {
            return TypedResults.Problem(
                title: "Sessão inválida",
                detail: "Refresh token ausente.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(
            new RefreshSessionCommand
            {
                RefreshToken = presented,
                SwitchToTenantPublicId = request?.SwitchToTenantId,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString()
            },
            cancellationToken);

        if (!result.Succeeded)
        {
            if (result.SessionTerminated)
            {
                // Cookie apagado no mesmo retorno: deixá-lo no browser faria o cliente
                // reapresentar um token revogado a cada tentativa, gerando ruído de
                // "reuso detectado" que não é mais sinal de nada.
                httpContext.Response.Cookies.Delete(RefreshCookieName);
            }

            return TypedResults.Problem(
                title: "Sessão inválida",
                detail: "Faça login novamente.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return BuildSessionResult(result.Session!, httpContext);
    }

    /// <summary>
    /// Monta a resposta divergindo por plataforma.
    /// </summary>
    /// <remarks>
    /// A divergência é decidida pelo servidor, não pelo cliente: no web o refresh
    /// token vai em cookie <c>HttpOnly</c> e some do corpo; no mobile vai no corpo,
    /// para ser guardado em Keychain/Keystore. Deixar o cliente escolher permitiria a
    /// um XSS pedir a variante que o JavaScript consegue ler.
    /// </remarks>
    private static Ok<SessionResponse> BuildSessionResult(
        AuthenticatedSession session,
        HttpContext httpContext)
    {
        bool isBrowser = IsBrowserClient(httpContext);

        if (isBrowser)
        {
            httpContext.Response.Cookies.Append(RefreshCookieName, session.RefreshToken, new CookieOptions
            {
                HttpOnly = true,                      // JavaScript não alcança
                Secure = true,                        // só por TLS
                SameSite = SameSiteMode.Strict,       // elimina a maior parte do CSRF
                Path = "/api/v1/auth",                // não viaja em requisições comuns
                Expires = session.RefreshTokenExpiresAt
            });
        }

        return TypedResults.Ok(new SessionResponse
        {
            AccessToken = session.AccessToken,
            ExpiresAt = session.AccessTokenExpiresAt,
            RefreshToken = isBrowser ? null : session.RefreshToken,
            UserId = session.UserPublicId,
            TenantId = session.TenantPublicId,
            Roles = session.Roles
        });
    }

    /// <summary>
    /// Distingue navegador de app nativo.
    /// </summary>
    /// <remarks>
    /// O app React Native envia <c>X-Congrega-Client: mobile</c>. A ausência do header
    /// é tratada como navegador — o padrão mais restritivo, já que assumir "mobile"
    /// por engano devolveria o refresh token no corpo para um contexto onde um XSS
    /// poderia lê-lo.
    /// </remarks>
    private static bool IsBrowserClient(HttpContext httpContext) =>
        !string.Equals(
            httpContext.Request.Headers["X-Congrega-Client"].ToString(),
            "mobile",
            StringComparison.OrdinalIgnoreCase);
}
