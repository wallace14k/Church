using Congrega.Domain.Billing;
using Congrega.Infrastructure.Locking;
using Congrega.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Congrega.Infrastructure.IntegrationTests;

/// <summary>
/// Prova a query de reivindicação de <see cref="PaymentWebhookRepository.ClaimBatchAsync"/>
/// contra um Postgres real.
/// </summary>
/// <remarks>
/// A query em si (<c>UPDATE ... FROM (SELECT ... FOR UPDATE SKIP LOCKED) ...
/// RETURNING</c>) compila em C# não importa o que ela realmente faça no banco —
/// o mesmo descompasso que já apareceu nesta base (<c>Subscription</c> sem
/// mapeamento, testado com dublê, só quebrou contra Postgres de verdade). O
/// teste mais importante aqui é o de bloqueio: sem ele, um erro de sintaxe na
/// cláusula <c>FOR UPDATE SKIP LOCKED</c> passaria despercebido até duas
/// réplicas do worker processarem o mesmo pagamento ao mesmo tempo em produção.
/// </remarks>
public sealed class PaymentWebhookRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("congrega")
        .WithUsername("congrega")
        .WithPassword("owner-" + Guid.NewGuid().ToString("N"))
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // payment_webhooks não tem RLS (ver comentário em ReceivePaymentWebhookHandler)
        // — não há necessidade de congrega_app/congrega_worker aqui, só da tabela
        // existir. Criada direto, sem rodar as migrations inteiras: mais rápido, e
        // a cobertura de "a migration cria a tabela certa" já é de
        // CrossTenantIsolationTests/os outros testes de integração.
        await using var conexao = new NpgsqlConnection(_container.GetConnectionString());
        await conexao.OpenAsync();

        await using var criar = new NpgsqlCommand(
            """
            CREATE TABLE payment_webhooks (
                id                 BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                provider           SMALLINT     NOT NULL,
                provider_event_id  VARCHAR(200) NOT NULL,
                event_type         VARCHAR(100) NOT NULL,
                payload            JSONB        NOT NULL,
                signature_valid    BOOLEAN      NOT NULL,
                received_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
                processed_at       TIMESTAMPTZ,
                process_attempts   SMALLINT     NOT NULL DEFAULT 0,
                last_error         TEXT,
                correlation_id     VARCHAR(40),

                CONSTRAINT uq_webhook_event UNIQUE (provider, provider_event_id)
            );
            """,
            conexao);
        await criar.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private PaymentWebhookRepository CreateRepository() => new(
        Options.Create(new DatabaseOptions
        {
            PooledConnectionString = _container.GetConnectionString(),
            DirectConnectionString = _container.GetConnectionString(),
        }));

    private async Task InserirAsync(
        string providerEventId,
        bool signatureValid,
        DateTimeOffset? processedAt = null,
        short processAttempts = 0)
    {
        await using var conexao = new NpgsqlConnection(_container.GetConnectionString());
        await conexao.OpenAsync();

        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO payment_webhooks
                (provider, provider_event_id, event_type, payload, signature_valid, processed_at, process_attempts)
            VALUES
                (1, @eventId, 'charge.updated', '{}'::jsonb, @signatureValid, @processedAt, @processAttempts);
            """,
            conexao);

        comando.Parameters.AddWithValue("eventId", providerEventId);
        comando.Parameters.AddWithValue("signatureValid", signatureValid);
        comando.Parameters.AddWithValue("processedAt", (object?)processedAt ?? DBNull.Value);
        comando.Parameters.AddWithValue("processAttempts", processAttempts);

        await comando.ExecuteNonQueryAsync();
    }

    private async Task<short> LerTentativasAsync(string providerEventId)
    {
        await using var conexao = new NpgsqlConnection(_container.GetConnectionString());
        await conexao.OpenAsync();

        await using var comando = new NpgsqlCommand(
            "SELECT process_attempts FROM payment_webhooks WHERE provider_event_id = @eventId;", conexao);
        comando.Parameters.AddWithValue("eventId", providerEventId);

        return (short)(await comando.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task So_reivindica_assinatura_valida_nao_processado_e_dentro_do_limite_de_tentativas()
    {
        await InserirAsync("evt_valido", signatureValid: true);
        await InserirAsync("evt_assinatura_invalida", signatureValid: false);
        await InserirAsync("evt_ja_processado", signatureValid: true, processedAt: DateTimeOffset.UtcNow);
        await InserirAsync("evt_tentativas_esgotadas", signatureValid: true, processAttempts: 6);

        var repositorio = CreateRepository();
        var lote = await repositorio.ClaimBatchAsync(batchSize: 10, maxAttempts: 6, CancellationToken.None);

        var reivindicados = lote.Select(w => w.ProviderEventId).ToList();
        Assert.Contains("evt_valido", reivindicados);
        Assert.DoesNotContain("evt_assinatura_invalida", reivindicados);
        Assert.DoesNotContain("evt_ja_processado", reivindicados);
        Assert.DoesNotContain("evt_tentativas_esgotadas", reivindicados);
    }

    [Fact]
    public async Task Reivindicar_incrementa_process_attempts()
    {
        await InserirAsync("evt_1", signatureValid: true);

        var repositorio = CreateRepository();
        await repositorio.ClaimBatchAsync(batchSize: 10, maxAttempts: 6, CancellationToken.None);

        Assert.Equal(1, await LerTentativasAsync("evt_1"));
    }

    [Fact]
    public async Task Respeita_o_tamanho_do_lote()
    {
        for (int i = 0; i < 5; i++)
        {
            await InserirAsync($"evt_{i}", signatureValid: true);
        }

        var repositorio = CreateRepository();
        var lote = await repositorio.ClaimBatchAsync(batchSize: 2, maxAttempts: 6, CancellationToken.None);

        Assert.Equal(2, lote.Count);
    }

    [Fact]
    public async Task Nao_reivindica_linha_travada_por_outra_transacao()
    {
        // O caso que justifica SKIP LOCKED em vez de SELECT puro: duas réplicas
        // do worker nunca podem pegar o mesmo evento. Aqui simulamos a segunda
        // réplica segurando o lock de uma linha numa transação aberta.
        await InserirAsync("evt_travado", signatureValid: true);
        await InserirAsync("evt_livre", signatureValid: true);

        await using var conexaoTravando = new NpgsqlConnection(_container.GetConnectionString());
        await conexaoTravando.OpenAsync();
        await using var transacao = await conexaoTravando.BeginTransactionAsync();

        await using (var travar = new NpgsqlCommand(
            "SELECT id FROM payment_webhooks WHERE provider_event_id = 'evt_travado' FOR UPDATE",
            conexaoTravando,
            transacao))
        {
            await using var leitor = await travar.ExecuteReaderAsync();
            await leitor.ReadAsync();
        }

        // A transação NÃO é commitada nem revertida ainda — o lock continua ativo
        // enquanto a reivindicação abaixo roda numa conexão totalmente separada.
        var repositorio = CreateRepository();
        var lote = await repositorio.ClaimBatchAsync(batchSize: 10, maxAttempts: 6, CancellationToken.None);

        var reivindicados = lote.Select(w => w.ProviderEventId).ToList();
        Assert.DoesNotContain("evt_travado", reivindicados);
        Assert.Contains("evt_livre", reivindicados);

        await transacao.RollbackAsync();
    }
}
