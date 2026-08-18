using Congrega.Application.Abstractions;

namespace Congrega.Workers;

/// <summary>
/// Contexto de tenant fixo para o processo de workers.
/// </summary>
/// <remarks>
/// O Workers roda com a role <c>congrega_worker</c> (BYPASSRLS) e não atua em
/// nome de nenhum usuário ou requisição específica — processa filas que, por
/// natureza, cruzam tenants (Outbox, retenção, webhooks de pagamento). Por isso
/// <see cref="IsCrossTenantOperation"/> é sempre <c>true</c>: os Global Query
/// Filters do EF Core não se aplicam, e <c>TenantConnectionInterceptor</c> nem
/// tenta definir <c>app.tenant_id</c>/<c>app.user_id</c> — definir um tenant
/// errado seria pior do que não definir nenhum, e o RLS já não bloquearia mesmo
/// que tentasse, porque a role tem BYPASSRLS.
/// </remarks>
internal sealed class WorkerTenantContext : ITenantContext
{
    public long? TenantId => null;
    public long? UserId => null;
    public bool IsCrossTenantOperation => true;
}
