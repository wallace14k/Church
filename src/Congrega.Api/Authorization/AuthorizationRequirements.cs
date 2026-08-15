using System.Security.Claims;
using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Congrega.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace Congrega.Api.Authorization;

/// <summary>
/// Exige e-mail verificado.
/// </summary>
/// <remarks>
/// Separado dos demais requisitos porque se aplica a quase tudo, mas não a tudo: o
/// próprio fluxo de verificação precisa funcionar sem ele.
/// </remarks>
public sealed class EmailVerifiedRequirement : IAuthorizationRequirement;

/// <summary>Exige que a requisição esteja dentro do contexto de um tenant válido.</summary>
public sealed class TenantScopedRequirement : IAuthorizationRequirement;

/// <summary>Exige uma permissão específica no tenant corrente.</summary>
public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

public sealed class EmailVerifiedHandler : AuthorizationHandler<EmailVerifiedRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EmailVerifiedRequirement requirement)
    {
        var claim = context.User.FindFirst(CongregaClaims.EmailVerified);

        if (claim is not null && bool.TryParse(claim.Value, out bool verified) && verified)
        {
            context.Succeed(requirement);
        }

        // Sem Fail() explícito: um requisito não satisfeito já reprova a policy.
        // Chamar Fail() tornaria a reprovação definitiva e impediria que outro
        // handler do mesmo requisito o satisfizesse — comportamento indesejado aqui.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Valida o tenant da requisição.
/// </summary>
/// <remarks>
/// Confia em <see cref="ITenantContext"/>, e não na claim crua. O contexto só é
/// populado pelo middleware <b>depois</b> de confirmar no banco que existe membership
/// ativa: a claim descreve a escolha do usuário, o banco decide a permissão.
/// </remarks>
public sealed class TenantScopedHandler(ITenantContext tenantContext)
    : AuthorizationHandler<TenantScopedRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantScopedRequirement requirement)
    {
        if (tenantContext.TenantId is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Valida permissão dentro do tenant corrente.
/// </summary>
/// <remarks>
/// <para>
/// As permissões vêm da claim <c>perms</c>, que é reconstruída a cada rotação de
/// refresh token — no máximo 15 minutos após uma revogação de papel. É esse ciclo
/// curto que torna desnecessária uma consulta ao banco por requisição.
/// </para>
/// <para>
/// <b>Permissão concede a classe da operação, nunca a instância.</b> Ter
/// <c>members.read</c> autoriza "ler membros"; qual membro específico continua sendo
/// verificado no handler, com filtro de tenant e checagem de posse. Sem essa segunda
/// etapa, a permissão viraria acesso irrestrito a toda a base.
/// </para>
/// </remarks>
public sealed class PermissionHandler(ITenantContext tenantContext)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Permissão sem tenant não significa nada: papéis são concedidos dentro de
        // uma membership. Um token sem tenant nunca satisfaz este requisito.
        if (tenantContext.TenantId is null)
        {
            return Task.CompletedTask;
        }

        bool hasPermission = context.User
            .FindAll(CongregaClaims.Permissions)
            .Any(c => string.Equals(c.Value, requirement.PermissionCode, StringComparison.Ordinal));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Políticas da aplicação.
/// </summary>
/// <remarks>
/// A composição responde à pergunta do briefing sobre como "Assinante Premium" difere
/// de "Administrador da Igreja": <see cref="PremiumContent"/> <b>não</b> exige tenant
/// nem papel algum, enquanto todas as policies de igreja exigem os dois. São eixos
/// ortogonais — ser assinante não dá acesso administrativo, e ser administrador não
/// dá conteúdo premium de graça.
/// </remarks>
public static class Policies
{
    public const string MembersRead = "Members.Read";
    public const string MembersWrite = "Members.Write";
    public const string GivingWrite = "Giving.Write";
    public const string ChildrenCheckout = "Children.Checkout";
    public const string PremiumContent = "Premium.Content";
    public const string BillingManage = "Billing.Manage";

    public static void Register(AuthorizationBuilder builder)
    {
        AddTenantPolicy(builder, MembersRead, Permissions.MembersRead);
        AddTenantPolicy(builder, MembersWrite, Permissions.MembersWrite);
        AddTenantPolicy(builder, GivingWrite, Permissions.GivingWrite);
        AddTenantPolicy(builder, ChildrenCheckout, Permissions.ChildrenCheckout);
        AddTenantPolicy(builder, BillingManage, Permissions.BillingManage);

        // Conteúdo premium: apenas identidade verificada. O direito de acesso é
        // resolvido por entitlement no handler do endpoint, consultando o banco —
        // nunca pela claim subscription_tier, que pode estar 15 minutos desatualizada
        // e concederia acesso após um cancelamento.
        builder.AddPolicy(PremiumContent, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new EmailVerifiedRequirement()));
    }

    private static void AddTenantPolicy(AuthorizationBuilder builder, string name, string permission) =>
        builder.AddPolicy(name, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(
                new EmailVerifiedRequirement(),
                new TenantScopedRequirement(),
                new PermissionRequirement(permission)));
}

/// <summary>Leitura tipada das claims do Congrega.</summary>
public static class ClaimsPrincipalExtensions
{
    public static long? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(CongregaClaims.Subject)?.Value;
        return long.TryParse(value, out long id) ? id : null;
    }

    public static long? GetTenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(CongregaClaims.TenantId)?.Value;
        return long.TryParse(value, out long id) ? id : null;
    }
}
