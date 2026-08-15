using Congrega.Domain.Retention;
using Congrega.Infrastructure.Locking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Congrega.Infrastructure.Notifications;

/// <summary>
/// Enfileira alertas em <c>notification_queue</c> e publica o evento de domínio
/// correspondente no Outbox — tudo em uma única instrução, portanto atômico.
/// </summary>
/// <remarks>
/// <para>
/// <b>Não envia nada.</b> Nenhuma chamada a SMTP, FCM ou APNs acontece aqui. O job
/// grava; um worker separado (o dispatcher do Outbox) entrega. Enviar dentro do job
/// traria dois defeitos: a latência do provedor entraria no tempo do ciclo,
/// segurando o lock distribuído, e uma falha após o commit deixaria alerta marcado
/// como enviado sem ter sido.
/// </para>
/// <para>
/// <b>Deduplicação é do banco.</b> O <c>ON CONFLICT (dedupe_key) DO NOTHING</c> é o
/// que garante o requisito de não repetir alerta. Três réplicas inserindo o mesmo
/// conjunto produzem uma inserção e duas colisões silenciosas. A alternativa —
/// consultar antes e inserir depois — é race condition sob concorrência, conforme a
/// §14 da skill de segurança.
/// </para>
/// <para>
/// <b>Set-based, sem N+1.</b> Um lote inteiro vai em <b>uma</b> ida ao banco via
/// <c>unnest</c> de arrays paralelos. Um laço com um INSERT por alerta faria 1.500
/// round-trips por lote — e é exatamente o padrão que o briefing veda.
/// </para>
/// <para>
/// A instrução é uma só, logo roda em transação implícita: ou fila e Outbox avançam
/// juntos, ou nenhum dos dois. Não há janela em que o alerta exista na fila sem o
/// evento correspondente.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    IOptions<DatabaseOptions> options,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    /// <summary>
    /// Teto de elementos por instrução. Arrays muito grandes aumentam o tempo de
    /// parse e o pico de memória do backend sem ganho de throughput.
    /// </summary>
    private const int MaxItemsPerStatement = 1_000;

    private const string EnqueueSql = """
        WITH inserted AS (
            INSERT INTO notification_queue
                (user_id, tenant_id, channel, template_code, payload, dedupe_key, correlation_id)
            SELECT t.user_id,
                   t.tenant_id,
                   t.channel,
                   t.template_code,
                   t.payload::jsonb,
                   t.dedupe_key,
                   $7
              FROM unnest($1::bigint[], $2::bigint[], $3::smallint[],
                          $4::text[],   $5::text[],   $6::text[])
                AS t(user_id, tenant_id, channel, template_code, payload, dedupe_key)
            ON CONFLICT (dedupe_key) DO NOTHING
            RETURNING id, user_id
        ),
        outboxed AS (
            INSERT INTO outbox_messages (message_type, payload, correlation_id)
            SELECT 'RetentionAlertEnqueued',
                   jsonb_build_object('notificationId', i.id, 'userId', i.user_id),
                   $7
              FROM inserted i
            RETURNING 1
        )
        SELECT count(*) FROM inserted;
        """;

    private readonly DatabaseOptions _options = options.Value;

    public async Task<int> DispatchAsync(
        IReadOnlyCollection<RetentionAlert> alerts,
        CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_options.PooledConnectionString);
        await connection.OpenAsync(cancellationToken);

        int totalEnqueued = 0;

        foreach (var chunk in alerts.Chunk(MaxItemsPerStatement))
        {
            totalEnqueued += await EnqueueChunkAsync(connection, chunk, cancellationToken);
        }

        int duplicates = alerts.Count - totalEnqueued;
        if (duplicates > 0)
        {
            logger.LogDebug(
                "{Duplicates} de {Total} alertas já existiam e foram descartados pela deduplicação.",
                duplicates, alerts.Count);
        }

        return totalEnqueued;
    }

    private static async Task<int> EnqueueChunkAsync(
        NpgsqlConnection connection,
        RetentionAlert[] chunk,
        CancellationToken cancellationToken)
    {
        int size = chunk.Length;

        // Arrays paralelos — a forma que unnest consome. Alocados no tamanho exato
        // do chunk para evitar cópia por crescimento.
        var userIds = new long[size];
        var tenantIds = new long?[size];
        var channels = new short[size];
        var templates = new string[size];
        var payloads = new string[size];
        var dedupeKeys = new string[size];

        for (int i = 0; i < size; i++)
        {
            var alert = chunk[i];
            userIds[i] = alert.UserId;
            tenantIds[i] = alert.TenantId;
            channels[i] = (short)alert.Channel;
            templates[i] = alert.TemplateCode;
            payloads[i] = alert.PayloadJson;
            dedupeKeys[i] = alert.DedupeKey;
        }

        await using var command = new NpgsqlCommand(EnqueueSql, connection);
        command.Parameters.Add(NewArray(userIds, NpgsqlDbType.Bigint));
        command.Parameters.Add(NewArray(tenantIds, NpgsqlDbType.Bigint));
        command.Parameters.Add(NewArray(channels, NpgsqlDbType.Smallint));
        command.Parameters.Add(NewArray(templates, NpgsqlDbType.Text));
        command.Parameters.Add(NewArray(payloads, NpgsqlDbType.Text));
        command.Parameters.Add(NewArray(dedupeKeys, NpgsqlDbType.Text));
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            NpgsqlDbType = NpgsqlDbType.Text
        });

        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is long count ? (int)count : 0;
    }

    private static NpgsqlParameter NewArray(Array value, NpgsqlDbType elementType) => new()
    {
        Value = value,
        NpgsqlDbType = NpgsqlDbType.Array | elementType
    };
}
