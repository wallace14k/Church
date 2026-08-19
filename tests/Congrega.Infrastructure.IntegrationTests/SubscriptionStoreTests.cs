using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Congrega.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Congrega.Infrastructure.IntegrationTests;

/// <summary>
/// <c>SubscriptionStore.FindCurrentByUserAsync</c> contra Postgres real.
/// </summary>
/// <remarks>
/// <para>
/// Existe por causa de um bug que chegou a rodar: o método chamava-se
/// <c>FindActiveByUserAsync</c> e filtrava
/// <c>Active | PastDue | Grace</c>, deixando <c>Canceled</c> de fora. Como
/// cancelar <b>não</b> revoga acesso — o direito vale até
/// <c>CurrentPeriodEnd</c> (§6 de <c>docs/03-arquitetura.md</c>) —, quem
/// cancelava e recarregava a tela recebia <c>hasSubscription: false</c> e via a
/// vitrine de planos, ainda dentro do período que tinha pago. Foi observado ao
/// vivo, cancelando pela API de verdade.
/// </para>
/// <para>
/// O teste é de integração, e não de unidade com dublê, porque o que estava
/// errado era a <b>consulta</b>. Um fake que devolve o que o teste mandou
/// guardar teria passado com o filtro errado no lugar.
/// </para>
/// </remarks>
public sealed class SubscriptionStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("congrega")
        .WithUsername("congrega")
        .WithPassword("owner-" + Guid.NewGuid().ToString("N"))
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private long _planId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        string conexao = _container.GetConnectionString();

        // citext precisa existir antes do primeiro NpgsqlDataSource — o Npgsql
        // resolve o catálogo de tipos de extensão uma vez, na construção.
        await using (var bootstrap = new NpgsqlConnection(conexao))
        {
            await bootstrap.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS citext;", bootstrap);
            await cmd.ExecuteNonQueryAsync();
        }

        _dataSource = NpgsqlDataSource.Create(conexao);

        await using (var migracao = CriarContexto())
        {
            await migracao.Database.MigrateAsync();
        }

        // Um plano B2C e um usuário: as FKs de `subscriptions` exigem os dois.
        await using var conn = await _dataSource.OpenConnectionAsync();

        await using (var plano = new NpgsqlCommand(
            """
            INSERT INTO plans (code, name, audience, billing_period, price_cents, currency, is_active)
            VALUES ('teste_mensal', 'Teste Mensal', 2, 1, 2990, 'BRL', TRUE)
            ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name
            RETURNING id;
            """, conn))
        {
            _planId = (long)(await plano.ExecuteScalarAsync())!;
        }

        await using var usuario = new NpgsqlCommand(
            """
            INSERT INTO users (email, full_name, status, email_verified)
            VALUES ('titular@teste.congrega', 'Titular de Teste', 1, TRUE)
            ON CONFLICT DO NOTHING;
            """, conn);
        await usuario.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private CongregaDbContext CriarContexto()
    {
        var opcoes = new DbContextOptionsBuilder<CongregaDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        // Contexto cross-tenant: o assinante Congrega+ não tem igreja, e o que
        // está sob teste é o filtro de STATUS, não o de tenant.
        return new CongregaDbContext(opcoes, ContextoDeWorker.Instance, TimeProvider.System);
    }

    private async Task<long> SemearAsync(SubscriptionStatus status)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        await using var limpar = new NpgsqlCommand("DELETE FROM subscriptions;", conn);
        await limpar.ExecuteNonQueryAsync();

        await using var inserir = new NpgsqlCommand(
            """
            INSERT INTO subscriptions
                (plan_id, user_id, status, source, current_period_start, current_period_end)
            VALUES
                (@planId,
                 (SELECT id FROM users WHERE email = 'titular@teste.congrega'),
                 @status, 1, now() - INTERVAL '5 days', now() + INTERVAL '25 days')
            RETURNING user_id;
            """, conn);

        inserir.Parameters.AddWithValue("planId", _planId);
        inserir.Parameters.AddWithValue("status", (short)status);

        return (long)(await inserir.ExecuteScalarAsync())!;
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Grace)]
    [InlineData(SubscriptionStatus.Canceled)]
    public async Task Devolve_assinatura_que_ainda_rege_o_usuario(SubscriptionStatus status)
    {
        // `Canceled` é o caso que o bug original omitia. Os outros três estão
        // aqui para que o teste também acuse se alguém "corrigir" ao contrário
        // e derrubar um dos que já funcionavam.
        long userId = await SemearAsync(status);

        await using var contexto = CriarContexto();
        var store = new SubscriptionStore(contexto);

        var encontrada = await store.FindCurrentByUserAsync(userId, CancellationToken.None);

        Assert.NotNull(encontrada);
        Assert.Equal(status, encontrada.Status);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Pending)]
    [InlineData(SubscriptionStatus.Expired)]
    public async Task Ignora_assinatura_que_nao_rege_mais_nada(SubscriptionStatus status)
    {
        // `Pending` nunca chegou a valer — quem cuida dela é
        // FindReusableForCheckoutAsync, no caminho do checkout. `Expired`
        // acabou: devolvê-la mostraria "sua assinatura" a quem não tem nenhuma.
        long userId = await SemearAsync(status);

        await using var contexto = CriarContexto();
        var store = new SubscriptionStore(contexto);

        Assert.Null(await store.FindCurrentByUserAsync(userId, CancellationToken.None));
    }

    /// <summary>Contexto sem tenant e sem usuário — igual ao do processo de workers.</summary>
    private sealed class ContextoDeWorker : ITenantContext
    {
        public static readonly ContextoDeWorker Instance = new();
        public long? TenantId => null;
        public long? UserId => null;
        public bool IsCrossTenantOperation => true;
    }
}
