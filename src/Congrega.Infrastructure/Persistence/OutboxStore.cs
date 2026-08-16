using Congrega.Application.Outbox;
using Congrega.Infrastructure.Locking;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Fila do Outbox sobre PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reivindicação por lease, em uma única instrução.</b> O
/// <c>UPDATE ... FROM (SELECT ... FOR UPDATE SKIP LOCKED) ... RETURNING</c>
/// resolve, atomicamente, o problema que normalmente exige um broker:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>SKIP LOCKED</c> — réplicas concorrentes pulam as linhas já travadas em
///     vez de esperar. Cada uma leva um conjunto disjunto, e a vazão cresce com o
///     número de pods. Sem ele, todas disputariam as mesmas linhas e
///     serializariam.
///   </description></item>
///   <item><description>
///     <c>next_attempt_at = now() + lease</c> — a mensagem sai da fila por um
///     tempo. Se o pod morrer no meio, o lease expira e outra réplica a retoma.
///     Sem lease, uma mensagem reivindicada por um pod que caiu ficaria parada
///     para sempre.
///   </description></item>
///   <item><description>
///     <c>attempts + 1</c> na própria reivindicação — o contador avança mesmo se
///     o processo for morto antes de qualquer marcação, o que impede que uma
///     mensagem capaz de derrubar o worker seja retentada infinitamente.
///   </description></item>
/// </list>
/// <para>
/// A transação é curta: fecha ao devolver o lote. O processamento acontece fora
/// dela, então uma entrega lenta não segura tuplas mortas nem atrapalha o
/// autovacuum.
/// </para>
/// </remarks>
internal sealed class OutboxStore(IOptions<DatabaseOptions> options) : IOutboxStore
{
    private const string ClaimSql = """
        UPDATE outbox_messages AS o
           SET attempts        = o.attempts + 1,
               next_attempt_at = now() + ($2 * INTERVAL '1 second')
          FROM (
                SELECT id
                  FROM outbox_messages
                 WHERE processed_at     IS NULL
                   AND dead_lettered_at IS NULL
                   AND next_attempt_at  <= now()
                 ORDER BY next_attempt_at
                 LIMIT $1
                 FOR UPDATE SKIP LOCKED
               ) AS claimed
         WHERE o.id = claimed.id
        RETURNING o.id, o.message_type, o.payload, o.attempts, o.correlation_id;
        """;

    private const string MarkProcessedSql = """
        UPDATE outbox_messages SET processed_at = now(), last_error = NULL WHERE id = $1;
        """;

    private const string MarkRetrySql = """
        UPDATE outbox_messages SET next_attempt_at = $2, last_error = $3 WHERE id = $1;
        """;

    private const string MarkDeadLetterSql = """
        UPDATE outbox_messages SET dead_lettered_at = now(), last_error = $2 WHERE id = $1;
        """;

    private readonly DatabaseOptions _options = options.Value;

    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        await using var conexao = await AbrirAsync(cancellationToken);
        await using var comando = new NpgsqlCommand(ClaimSql, conexao);
        comando.Parameters.Add(new NpgsqlParameter<int> { TypedValue = batchSize });
        comando.Parameters.Add(new NpgsqlParameter<double> { TypedValue = leaseDuration.TotalSeconds });

        var reivindicadas = new List<OutboxEnvelope>(batchSize);

        await using var leitor = await comando.ExecuteReaderAsync(cancellationToken);
        while (await leitor.ReadAsync(cancellationToken))
        {
            reivindicadas.Add(new OutboxEnvelope
            {
                Id = leitor.GetInt64(0),
                MessageType = leitor.GetString(1),
                Payload = leitor.GetString(2),
                Attempts = leitor.GetInt16(3),
                CorrelationId = await leitor.IsDBNullAsync(4, cancellationToken)
                    ? null
                    : leitor.GetString(4),
            });
        }

        return reivindicadas;
    }

    public Task MarkProcessedAsync(long messageId, CancellationToken cancellationToken) =>
        ExecutarAsync(MarkProcessedSql, cancellationToken,
            new NpgsqlParameter<long> { TypedValue = messageId });

    public Task MarkForRetryAsync(
        long messageId,
        string failure,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        ExecutarAsync(MarkRetrySql, cancellationToken,
            new NpgsqlParameter<long> { TypedValue = messageId },
            new NpgsqlParameter<DateTimeOffset> { TypedValue = nextAttemptAt },
            new NpgsqlParameter<string> { TypedValue = failure });

    public Task MarkDeadLetteredAsync(long messageId, string failure, CancellationToken cancellationToken) =>
        ExecutarAsync(MarkDeadLetterSql, cancellationToken,
            new NpgsqlParameter<long> { TypedValue = messageId },
            new NpgsqlParameter<string> { TypedValue = failure });

    private async Task ExecutarAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parametros)
    {
        await using var conexao = await AbrirAsync(cancellationToken);
        await using var comando = new NpgsqlCommand(sql, conexao);

        foreach (var parametro in parametros)
        {
            comando.Parameters.Add(parametro);
        }

        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var conexao = new NpgsqlConnection(_options.PooledConnectionString);
        await conexao.OpenAsync(cancellationToken);
        return conexao;
    }
}

/// <summary>Grava eventos de segurança em <c>security_events</c>.</summary>
internal sealed class SecurityEventStore(IOptions<DatabaseOptions> options) : ISecurityEventStore
{
    private const string InsertSql = """
        INSERT INTO security_events (user_id, event_type, severity, metadata, correlation_id, occurred_at)
        VALUES ($1, $2, $3, $4::jsonb, $5, $6);
        """;

    private readonly DatabaseOptions _options = options.Value;

    public async Task RecordAsync(SecurityEventRecord record, CancellationToken cancellationToken)
    {
        await using var conexao = new NpgsqlConnection(_options.PooledConnectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = new NpgsqlCommand(InsertSql, conexao);
        comando.Parameters.Add(Opcional(record.UserId, NpgsqlDbType.Bigint));
        comando.Parameters.Add(new NpgsqlParameter<string> { TypedValue = record.EventType });
        comando.Parameters.Add(new NpgsqlParameter<short> { TypedValue = record.Severity });
        comando.Parameters.Add(Opcional(record.Metadata, NpgsqlDbType.Text));
        comando.Parameters.Add(Opcional(
            System.Diagnostics.Activity.Current?.TraceId.ToString(), NpgsqlDbType.Text));
        comando.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = record.OccurredAt });

        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter Opcional(object? valor, NpgsqlDbType tipo) =>
        new() { Value = valor ?? DBNull.Value, NpgsqlDbType = tipo };
}

/// <summary>
/// Resolve contato do usuário para envios fora do fluxo de autenticação.
/// </summary>
/// <remarks>
/// Devolve <c>null</c> para conta anonimizada. Enviar e-mail para um endereço
/// <c>anon-*@removido.congrega.app</c> não só falharia como poderia caracterizar
/// tratamento de dado de titular que exerceu o direito de exclusão.
/// </remarks>
internal sealed class UserContactResolver(IOptions<DatabaseOptions> options) : IUserContactResolver
{
    private const string SelectSql = """
        SELECT email::text, full_name
          FROM users
         WHERE id = $1
           AND anonymized_at IS NULL
           AND status = 1;
        """;

    private readonly DatabaseOptions _options = options.Value;

    public async Task<UserContact?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        await using var conexao = new NpgsqlConnection(_options.PooledConnectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = new NpgsqlCommand(SelectSql, conexao);
        comando.Parameters.Add(new NpgsqlParameter<long> { TypedValue = userId });

        await using var leitor = await comando.ExecuteReaderAsync(cancellationToken);

        if (!await leitor.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserContact
        {
            Email = leitor.GetString(0),
            FullName = leitor.GetString(1),
        };
    }
}
