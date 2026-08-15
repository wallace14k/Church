using Congrega.Domain.Billing;
using Congrega.Domain.Retention;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Implementação da varredura de retenção.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que ADO.NET puro e não LINQ do EF Core.</b> O EF Core é o ORM do sistema e
/// atende bem a esmagadora maioria dos casos. Esta query específica foge do padrão
/// por três motivos concretos, e a skill <c>dotnet-expert</c> recomenda avaliar o SQL
/// gerado em consultas críticas em vez de aceitá-lo às cegas:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Uso do índice.</b> O filtro precisa cair em
///     <c>ix_sub_retention (status, current_period_end) WHERE status IN (2,3,4)</c>.
///     Qualquer função aplicada à coluna no <c>WHERE</c> — inclusive a conversão de
///     fuso — tornaria o predicado não-sargable e forçaria Seq Scan na tabela inteira.
///     Por isso o filtro usa <c>timestamptz</c> cru, com folga de um dia em cada
///     ponta, e a conversão para data local acontece apenas na projeção.
///   </description></item>
///   <item><description>
///     <b>LATERAL para resolver destinatários.</b> Assinatura pessoal tem um
///     destinatário; assinatura de igreja tem todos os administradores do tenant.
///     Expressar isso em LINQ produziria duas queries ou um JOIN com <c>OR</c> que o
///     planner não otimiza bem.
///   </description></item>
///   <item><description>
///     <b>Keyset pagination sobre assinaturas.</b> O <c>LIMIT</c> precisa se aplicar
///     a assinaturas, não a linhas de destinatário, sob pena de cortar um tenant no
///     meio e deixar administradores sem alerta.
///   </description></item>
/// </list>
/// <para>
/// A folga de um dia em cada ponta é intencional: linhas fora da faixa útil apenas
/// fazem <c>RetentionWindowCalculator.Resolve</c> devolver <c>null</c> e são
/// descartadas sem custo. Trocamos algumas linhas a mais por um predicado que usa
/// índice — o negócio certo.
/// </para>
/// </remarks>
public sealed class SubscriptionRepository(IOptions<Locking.DatabaseOptions> options) : ISubscriptionRepository
{
    private const string RetentionCandidatesSql = """
        WITH paged AS (
            SELECT s.id,
                   s.tenant_id,
                   s.user_id,
                   s.plan_id,
                   s.current_period_end,
                   s.status
              FROM subscriptions s
             WHERE s.status IN (2, 3, 4)          -- Active, PastDue, Grace
               AND s.current_period_end >= $1
               AND s.current_period_end <  $2
               AND s.id > $3
             ORDER BY s.id
             LIMIT $4
        )
        SELECT p.id,
               rec.user_id,
               p.tenant_id,
               rec.email,
               rec.full_name,
               pl.code,
               (p.current_period_end AT TIME ZONE $5::text)::date AS period_end_local,
               p.status
          FROM paged p
          JOIN plans pl ON pl.id = p.plan_id
          JOIN LATERAL (
                  -- Assinatura pessoal (Congrega+): o destinatário é o assinante.
                  SELECT u.id AS user_id, u.email::text AS email, u.full_name
                    FROM users u
                   WHERE p.user_id IS NOT NULL
                     AND u.id = p.user_id
                     AND u.status = 1

                  UNION ALL

                  -- Assinatura de igreja (ChMS): todos os administradores ativos.
                  SELECT u.id, u.email::text, u.full_name
                    FROM memberships m
                    JOIN user_roles ur ON ur.membership_id = m.id
                    JOIN roles r       ON r.id = ur.role_id
                    JOIN users u       ON u.id = m.user_id
                   WHERE p.tenant_id IS NOT NULL
                     AND m.tenant_id = p.tenant_id
                     AND m.status = 1
                     AND r.code = 'ChurchAdmin'
                     AND u.status = 1
               ) AS rec ON TRUE
         ORDER BY p.id, rec.user_id;
        """;

    private readonly Locking.DatabaseOptions _options = options.Value;

    public async Task<IReadOnlyList<RetentionCandidate>> GetRetentionCandidatesAsync(
        DateOnly periodEndFrom,
        DateOnly periodEndTo,
        long afterSubscriptionId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        // Folga de um dia antes e dois depois cobre qualquer deslocamento entre o
        // instante UTC persistido e a data local usada na regra de negócio.
        var lowerBound = new DateTimeOffset(
            periodEndFrom.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var upperBound = new DateTimeOffset(
            periodEndTo.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        await using var connection = new NpgsqlConnection(_options.PooledConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(RetentionCandidatesSql, connection);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = lowerBound });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = upperBound });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = afterSubscriptionId });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = batchSize });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = TimeZoneName });

        var results = new List<RetentionCandidate>(batchSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RetentionCandidate
            {
                SubscriptionId = reader.GetInt64(0),
                UserId = reader.GetInt64(1),
                TenantId = await reader.IsDBNullAsync(2, cancellationToken)
                    ? null
                    : reader.GetInt64(2),
                UserEmail = reader.GetString(3),
                UserFullName = reader.GetString(4),
                PlanCode = reader.GetString(5),
                PeriodEnd = reader.GetFieldValue<DateOnly>(6),
                Status = (SubscriptionStatus)reader.GetInt16(7)
            });
        }

        return results;
    }

    // Fuso usado na conversão da projeção. Mantido alinhado a
    // RetentionOptions.BusinessTimeZone; divergir faria a data local calculada no
    // banco discordar da data usada pelo calculador de janelas.
    private const string TimeZoneName = "America/Sao_Paulo";
}
