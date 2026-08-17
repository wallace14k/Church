using System.Globalization;
using Congrega.Domain.Billing;
using Congrega.Infrastructure.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Registro de webhooks recebidos.
/// </summary>
/// <remarks>
/// <para>
/// SQL direto, e não EF: a tabela <c>payment_webhooks</c> não é mapeada no
/// modelo (é uma das 15 fora das 11 entidades), e o que se precisa dela é
/// exatamente um <c>INSERT ... ON CONFLICT DO NOTHING</c> — que o EF não
/// expressa sem truque.
/// </para>
/// <para>
/// <b>A deduplicação é a constraint</b> <c>uq_webhook_event (provider,
/// provider_event_id)</c>, não uma consulta prévia. Consultar-e-depois-inserir
/// tem janela: dois webhooks idênticos chegando ao mesmo tempo em réplicas
/// diferentes passariam os dois pelo <c>SELECT</c> e inseririam os dois. É a
/// mesma razão pela qual o alerta de retenção deduplica por
/// <c>UNIQUE (dedupe_key)</c> e não por lock distribuído: locks falham,
/// constraints não.
/// </para>
/// </remarks>
internal sealed class PaymentWebhookRepository(IOptions<DatabaseOptions> options)
    : IPaymentWebhookRepository
{
    private readonly string _connectionString = options.Value.PooledConnectionString;

    public async Task<bool> TryRecordAsync(
        ReceivedWebhook webhook,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO payment_webhooks
                (provider, provider_event_id, event_type, payload, signature_valid, correlation_id)
            VALUES
                (@provider, @eventId, @eventType, @payload::jsonb, @signatureValid, @correlationId)
            ON CONFLICT (provider, provider_event_id) DO NOTHING;
            """;

        await using var conexao = new NpgsqlConnection(_connectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = new NpgsqlCommand(sql, conexao);
        comando.Parameters.AddWithValue("provider", (short)webhook.Provider);
        comando.Parameters.AddWithValue("eventId", webhook.ProviderEventId);
        comando.Parameters.AddWithValue("eventType", webhook.EventType);
        comando.Parameters.AddWithValue("payload", webhook.Payload);
        comando.Parameters.AddWithValue("signatureValid", webhook.SignatureValid);
        comando.Parameters.AddWithValue(
            "correlationId", (object?)webhook.CorrelationId ?? DBNull.Value);

        // 1 linha afetada = inédito. 0 = o ON CONFLICT engoliu, ou seja,
        // reentrega.
        int afetadas = await comando.ExecuteNonQueryAsync(cancellationToken);
        return afetadas == 1;
    }

    public async Task MarkProcessedAsync(
        WebhookProvider provider,
        string providerEventId,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE payment_webhooks
               SET processed_at = @processedAt,
                   process_attempts = process_attempts + 1,
                   last_error = NULL
             WHERE provider = @provider AND provider_event_id = @eventId;
            """;

        await ExecutarAsync(
            sql,
            cancellationToken,
            ("provider", (short)provider),
            ("eventId", providerEventId),
            ("processedAt", processedAt));
    }

    public async Task MarkFailedAsync(
        WebhookProvider provider,
        string providerEventId,
        string failureReason,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE payment_webhooks
               SET process_attempts = process_attempts + 1,
                   last_error = @erro
             WHERE provider = @provider AND provider_event_id = @eventId;
            """;

        await ExecutarAsync(
            sql,
            cancellationToken,
            ("provider", (short)provider),
            ("eventId", providerEventId),
            ("erro", Truncar(failureReason, 2000)));
    }

    private async Task ExecutarAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Nome, object Valor)[] parametros)
    {
        await using var conexao = new NpgsqlConnection(_connectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = new NpgsqlCommand(sql, conexao);
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Truncar(string valor, int max) =>
        valor.Length <= max ? valor : valor[..max];
}

/// <summary>Leitura de planos. SQL direto pelo mesmo motivo: tabela não mapeada.</summary>
internal sealed class PlanRepository(IOptions<DatabaseOptions> options) : IPlanRepository
{
    private readonly string _connectionString = options.Value.PooledConnectionString;

    // Duas constantes em vez de um WHERE interpolado. Os dois filtros possíveis
    // são literais do próprio código e a chave já iria como parâmetro — mas
    // montar SQL por concatenação é o padrão que o SAST sinaliza e que, um dia,
    // alguém estende passando um filtro vindo de fora. Não vale a economia.
    private const string SqlPorCodigo = """
        SELECT id, code, name, price_cents, billing_period
          FROM plans
         WHERE code = @chave AND is_active
         LIMIT 1;
        """;

    private const string SqlPorId = """
        SELECT id, code, name, price_cents, billing_period
          FROM plans
         WHERE id = @chave AND is_active
         LIMIT 1;
        """;

    public Task<PlanSnapshot?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        BuscarAsync(SqlPorCodigo, code, cancellationToken);

    public Task<PlanSnapshot?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
        BuscarAsync(SqlPorId, id, cancellationToken);

    private async Task<PlanSnapshot?> BuscarAsync(
        string sql,
        object chave,
        CancellationToken cancellationToken)
    {
        await using var conexao = new NpgsqlConnection(_connectionString);
        await conexao.OpenAsync(cancellationToken);

        await using var comando = new NpgsqlCommand(sql, conexao);
        comando.Parameters.AddWithValue("chave", chave);

        await using var leitor = await comando.ExecuteReaderAsync(cancellationToken);

        if (!await leitor.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlanSnapshot
        {
            Id = leitor.GetInt64(0),
            Code = leitor.GetString(1),
            Name = leitor.GetString(2),
            PriceCents = leitor.GetInt64(3),
            BillingPeriod = leitor.GetInt16(4),
        };
    }
}
