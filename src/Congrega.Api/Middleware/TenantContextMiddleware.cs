using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Identity;
using Congrega.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace Congrega.Api.Middleware;

/// <summary>Contexto da requisição corrente. Registrado como <c>scoped</c>.</summary>
internal sealed class RequestTenantContext : ITenantContext
{
    public long? TenantId { get; private set; }
    public long? UserId { get; private set; }

    /// <summary>
    /// Sempre <c>false</c> em requisição HTTP.
    /// </summary>
    /// <remarks>
    /// Não há setter público de propósito: operação cross-tenant é privilégio de
    /// worker, e um endpoint jamais deve conseguir ativá-la. Em produção esses
    /// processos rodam com a role <c>congrega_worker</c>, então o RLS bloquearia
    /// mesmo se alguém encontrasse um caminho.
    /// </remarks>
    public bool IsCrossTenantOperation => false;

    internal void Assign(long? userId, long? tenantId)
    {
        UserId = userId;
        TenantId = tenantId;
    }
}

internal sealed class HostEnvironmentAccessor(IWebHostEnvironment environment) : IHostEnvironmentAccessor
{
    public bool IsDevelopment => environment.IsDevelopment();
}

/// <summary>
/// Popula o contexto de tenant a partir do token, <b>validando contra o banco</b>.
/// </summary>
/// <remarks>
/// <para>
/// Este middleware é a materialização da regra central do isolamento: a claim
/// <c>tenant_id</c> diz qual tenant o usuário <i>selecionou</i>; a tabela
/// <c>memberships</c> diz se ele <i>pode</i>. Um token com assinatura válida cuja
/// membership foi revogada há dois minutos é recusado aqui.
/// </para>
/// <para>
/// <b>Custo e cache.</b> Sem cache, isso seria uma query por requisição. O cache de
/// 60 segundos limita o prejuízo: uma revogação de vínculo demora no máximo um minuto
/// para valer, o que é aceitável para papéis operacionais. Para revogação imediata
/// existe o caminho explícito de revogar os refresh tokens do usuário, que derruba a
/// sessão na próxima rotação.
/// </para>
/// </remarks>
internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan MembershipCacheLifetime = TimeSpan.FromSeconds(60);

    public async Task InvokeAsync(
        HttpContext context,
        RequestTenantContext tenantContext,
        IMembershipRepository memberships,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        long? userId = context.User.GetUserId();
        long? claimedTenantId = context.User.GetTenantId();

        if (userId is null)
        {
            // Token autenticado sem "sub" utilizável é token malformado, não sessão
            // anônima. Prosseguir sem contexto seria tratar um token quebrado como
            // requisição pública.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        long? effectiveTenantId = null;

        if (claimedTenantId is { } tenantId)
        {
            string cacheKey = $"membership:{userId}:{tenantId}";

            bool isMember = await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = MembershipCacheLifetime;
                var membership = await memberships.FindActiveAsync(
                    userId.Value, tenantId, context.RequestAborted);
                return membership is not null;
            });

            if (isMember)
            {
                effectiveTenantId = tenantId;
            }
            else
            {
                // Não é 403: a requisição segue sem tenant. Endpoints que exigem
                // tenant vão reprovar na policy, e endpoints de Congrega+ continuam
                // funcionando — o usuário perdeu o vínculo com a igreja, não a conta.
                context.Response.Headers.Append("X-Tenant-Context", "revoked");
            }
        }

        tenantContext.Assign(userId, effectiveTenantId);

        await next(context);
    }
}
