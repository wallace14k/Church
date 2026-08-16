using Congrega.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Resolve o tier de assinatura pessoal para a claim <c>subscription_tier</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Só alimenta a interface.</b> A decisão de acesso a conteúdo consulta
/// <c>entitlements</c> a cada requisição — nunca esta claim, que pode estar até 15
/// minutos desatualizada e concederia acesso após um cancelamento.
/// </para>
/// <para>
/// Filtra por assinatura <b>pessoal</b> (<c>user_id</c>), nunca de igreja: o plano do
/// ChMS é do tenant e não dá acesso ao Congrega+. Confundir os dois liberaria o
/// catálogo premium para todo membro de igreja cliente.
/// </para>
/// <para>
/// Devolve <c>null</c> quando não há assinatura ativa, e a claim é omitida do token —
/// ausência é inequívoca, enquanto <c>"subscription_tier": null</c> convidaria o
/// cliente a tratar null como um tier válido.
/// </para>
/// </remarks>
internal sealed class SubscriptionTierProvider(CongregaDbContext db) : ISubscriptionTierProvider
{
    public async Task<string?> GetActiveTierAsync(long userId, CancellationToken cancellationToken)
    {
        // Estados 2, 3 e 4 = Active, PastDue e Grace. PastDue e Grace continuam
        // valendo de propósito: o usuário pagou pelo período corrente e cortar o
        // acesso no primeiro erro de cobrança é a forma mais rápida de transformar
        // uma falha de cartão em cancelamento.
        // O alias "Value" é exigência do `SqlQuery<T>`: ele projeta o resultado em
        // uma coluna com esse nome exato. Sem o alias, o PostgreSQL responde
        // `42703: column s.Value does not exist` — erro que só aparece em
        // execução, porque a consulta é montada como string.
        return await db.Database
            .SqlQuery<string?>($"""
                SELECT p.code AS "Value"
                  FROM subscriptions s
                  JOIN plans p ON p.id = s.plan_id
                 WHERE s.user_id = {userId}
                   AND s.status IN (2, 3, 4)
                   AND s.current_period_end > now()
                 ORDER BY s.current_period_end DESC
                 LIMIT 1
                """)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
