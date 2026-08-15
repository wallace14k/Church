namespace Congrega.Application.Abstractions;

/// <summary>
/// Contexto de tenant e usuário da requisição corrente.
/// </summary>
/// <remarks>
/// <para>
/// Preenchido por middleware <b>depois</b> de validar que existe membership ativa
/// entre o usuário e o tenant da claim. A claim diz qual tenant o usuário
/// selecionou; este contexto só é populado com o que o banco confirmou.
/// </para>
/// <para>
/// É consumido em dois lugares, e a redundância é intencional:
/// </para>
/// <list type="number">
///   <item><description>
///     Global Query Filters do EF Core — a <b>autoridade</b> de isolamento.
///   </description></item>
///   <item><description>
///     Interceptor de conexão, que emite <c>SET LOCAL app.tenant_id</c> para as
///     policies de RLS — a <b>rede de segurança</b> para quando um filtro for
///     esquecido.
///   </description></item>
/// </list>
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// Tenant da requisição. <c>null</c> em requisição sem igreja — assinante
    /// Congrega+, endpoints públicos e jobs que cruzam tenants legitimamente.
    /// </summary>
    long? TenantId { get; }

    long? UserId { get; }

    /// <summary>
    /// Quando <c>true</c>, os Global Query Filters não se aplicam.
    /// </summary>
    /// <remarks>
    /// Reservado a workers que precisam cruzar tenants por natureza (faturamento,
    /// retenção). <b>Nunca</b> deve ser ligado a partir de uma requisição HTTP: em
    /// produção esses processos rodam com a role <c>congrega_worker</c>, e o RLS
    /// continuaria bloqueando mesmo se alguém tentasse.
    /// </remarks>
    bool IsCrossTenantOperation { get; }
}
