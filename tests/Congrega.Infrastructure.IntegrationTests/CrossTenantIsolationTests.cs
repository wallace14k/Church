using Congrega.Application.Abstractions;
using Congrega.Domain.Congregation;
using Congrega.Domain.Tenancy;
using Congrega.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Congrega.Infrastructure.IntegrationTests;

/// <summary>
/// O portão de saída da Onda 1 (docs/06-riscos-e-ondas.md) e a mitigação de R5
/// prometida no ADR-006: prova que um vazamento cross-tenant não depende de
/// ninguém lembrar de filtrar.
/// </summary>
/// <remarks>
/// <para>
/// O cenário reproduzido é exatamente o que o ADR-006 descreve como risco: "um
/// <c>FromSqlRaw</c>, um <c>IgnoreQueryFilters()</c> colocado para resolver um bug
/// de relatório, uma entidade nova cadastrada sem filtro". Aqui isso é simulado
/// de propósito — <c>IgnoreQueryFilters()</c> chamado direto — com a conexão
/// usando exatamente a role que a API usa em produção (<c>congrega_app</c>, sem
/// <c>BYPASSRLS</c>). Se o RLS estiver de fato armado, a query continua vendo só
/// o tenant do contexto. Se não estiver — por exemplo, se a API algum dia voltar
/// a conectar com a role dona das tabelas, que o PostgreSQL deixa atravessar RLS
/// por padrão — este teste vaza dado do outro tenant e falha.
/// </para>
/// <para>
/// Container Postgres 17 de verdade, migrado pelo mesmo código que roda em
/// produção (<c>CongregaDbContext.Database.MigrateAsync()</c>), incluindo as
/// migrations <c>AppRoles</c> e <c>MembershipsSelfServiceRls</c> — as mesmas que
/// fecharam a lacuna real encontrada ao escrever este teste: as roles nunca
/// tinham sido criadas, e a API rodava com a credencial dona das tabelas, o que
/// tornava o RLS inteiro decorativo.
/// </para>
/// </remarks>
public sealed class CrossTenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("congrega")
        .WithUsername("congrega")
        .WithPassword("owner-" + Guid.NewGuid().ToString("N"))
        .Build();

    // Alfanumérico puro: entra em ALTER ROLE ... PASSWORD por interpolação direta
    // (Postgres não parametriza DDL), e sem aspas nem ';' não há o que escapar.
    private readonly string _appPassword = "app-" + Guid.NewGuid().ToString("N");
    private readonly string _workerPassword = "worker-" + Guid.NewGuid().ToString("N");

    private NpgsqlDataSource _ownerDataSource = null!;
    private NpgsqlDataSource _appDataSource = null!;
    private long _tenantAId;
    private long _tenantBId;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        string ownerConnectionString = _container.GetConnectionString();

        // citext precisa existir ANTES do primeiro NpgsqlDataSource ser
        // construído para esta string. O Npgsql resolve o catálogo de tipos de
        // extensão uma vez, na construção do datasource, e mantém em cache pelo
        // tempo de vida dele — não por conexão física reaberta depois. Por isso
        // a extensão é criada numa conexão solta, descartável, antes de existir
        // qualquer NpgsqlDataSource "de verdade" apontando para este banco.
        await using (var bootstrap = new NpgsqlConnection(ownerConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS citext;", bootstrap);
            await cmd.ExecuteNonQueryAsync();
        }

        _ownerDataSource = NpgsqlDataSource.Create(ownerConnectionString);

        await using (var migrationContext = CreateContext(_ownerDataSource, CrossTenantFakeContext.Instance))
        {
            // A migration AppRoles cria congrega_app/congrega_worker SEM senha —
            // de propósito, para não gravar segredo em migration compilada. Aqui
            // definimos a senha do próprio teste, o equivalente de
            // db/010_bootstrap_roles.sql fora de produção.
            await migrationContext.Database.MigrateAsync();
        }

        await using (var connection = await _ownerDataSource.OpenConnectionAsync())
        {
            await using var alterApp = new NpgsqlCommand(
                $"ALTER ROLE congrega_app WITH PASSWORD '{_appPassword}'", connection);
            await alterApp.ExecuteNonQueryAsync();

            await using var alterWorker = new NpgsqlCommand(
                $"ALTER ROLE congrega_worker WITH PASSWORD '{_workerPassword}'", connection);
            await alterWorker.ExecuteNonQueryAsync();
        }

        _appDataSource = NpgsqlDataSource.Create(BuildConnectionString("congrega_app", _appPassword));

        // Duas igrejas e um membro em cada, semeados com a role dona (RLS não se
        // aplica a ela) — este é código de fixture, não o que está sob teste.
        await using var seedContext = CreateContext(_ownerDataSource, CrossTenantFakeContext.Instance);

        var tenantA = Tenant.Create("Igreja A", "igreja-a", DateTimeOffset.UtcNow);
        var tenantB = Tenant.Create("Igreja B", "igreja-b", DateTimeOffset.UtcNow);
        seedContext.Add(tenantA);
        seedContext.Add(tenantB);
        await seedContext.SaveChangesAsync();

        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;

        seedContext.Add(Member.Register(_tenantAId, "Alice da Igreja A", DateTimeOffset.UtcNow));
        seedContext.Add(Member.Register(_tenantBId, "Bob da Igreja B", DateTimeOffset.UtcNow));
        await seedContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _appDataSource.DisposeAsync();
        await _ownerDataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task IgnoreQueryFilters_ComRoleDeAplicacao_NaoVazaMembroDeOutroTenant()
    {
        // Contexto fixo no tenant A — exatamente o que a API teria depois de
        // resolver a membership no login.
        var tenantContext = new FixedTenantContext(_tenantAId, userId: null, isCrossTenantOperation: false);

        await using var context = CreateContext(_appDataSource, tenantContext);

        // O ataque simulado: alguém esqueceu o filtro, ou colocou
        // IgnoreQueryFilters() para "resolver rápido" um relatório. Do lado do EF
        // Core, esta consulta pede TODOS os membros, de qualquer tenant.
        var resultado = await context.Members.IgnoreQueryFilters().ToListAsync();

        // Se o RLS estiver armado, o Postgres nunca devolveu a linha do tenant B
        // — não é o cliente .NET filtrando depois, é a linha nunca tendo saído do
        // banco. Por isso a asserção é sobre o próprio resultado retornado, não
        // sobre uma dedução do que "deveria" ter sido filtrado.
        Assert.All(resultado, membro => Assert.Equal(_tenantAId, membro.TenantId));
        Assert.Contains(resultado, m => m.FullName == "Alice da Igreja A");
        Assert.DoesNotContain(resultado, m => m.FullName == "Bob da Igreja B");
    }

    [Fact]
    public async Task ComFiltroLigado_RoleDeAplicacao_VeApenasOProprioTenant()
    {
        var tenantContext = new FixedTenantContext(_tenantBId, userId: null, isCrossTenantOperation: false);

        await using var context = CreateContext(_appDataSource, tenantContext);

        // Sanidade: com o Global Query Filter normal (autoridade) mais o RLS
        // (rede de segurança) as duas camadas concordam — nenhuma vê o tenant A.
        var resultado = await context.Members.ToListAsync();

        Assert.Single(resultado);
        Assert.Equal("Bob da Igreja B", resultado[0].FullName);
    }

    [Fact]
    public async Task RoleDona_AtravessaRls_PorIssoAApiNuncaPodeUsaLa()
    {
        // Documenta o motivo de existir a migration AppRoles: a role que criou as
        // tabelas (aqui, "congrega", a mesma da string de conexão do container)
        // atravessa RLS por padrão no PostgreSQL — ENABLE ROW LEVEL SECURITY não
        // vale para o dono. Antes desta migration, era exatamente essa role que a
        // API usava.
        var tenantContext = new FixedTenantContext(_tenantAId, userId: null, isCrossTenantOperation: false);

        await using var context = CreateContext(_ownerDataSource, tenantContext);

        var resultado = await context.Members.IgnoreQueryFilters().ToListAsync();

        // As duas igrejas aparecem — não porque o teste está errado, mas porque
        // esta é precisamente a configuração perigosa que a migration AppRoles
        // fecha. Este teste existe para que, se algum dia a string de conexão da
        // API voltar a apontar para a role dona, o CI tenha uma falha explícita
        // em vez de descobrir o vazamento em produção.
        Assert.Contains(resultado, m => m.FullName == "Alice da Igreja A");
        Assert.Contains(resultado, m => m.FullName == "Bob da Igreja B");
    }

    private string BuildConnectionString(string username, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = username,
            Password = password
        };
        return builder.ConnectionString;
    }

    private static CongregaDbContext CreateContext(NpgsqlDataSource dataSource, ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<CongregaDbContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(new TenantConnectionInterceptor(tenantContext))
            .Options;

        return new CongregaDbContext(options, tenantContext, TimeProvider.System);
    }

    /// <summary>Contexto fixo, para simular a requisição já autenticada de um tenant.</summary>
    private sealed class FixedTenantContext(long? tenantId, long? userId, bool isCrossTenantOperation)
        : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
        public long? UserId { get; } = userId;
        public bool IsCrossTenantOperation { get; } = isCrossTenantOperation;
    }

    /// <summary>
    /// Usado só para migrar e semear. <c>IsCrossTenantOperation = true</c> desliga
    /// o Global Query Filter — necessário para inserir membros de dois tenants
    /// diferentes na mesma sessão de setup — e a conexão como dona das tabelas
    /// ignora RLS de qualquer forma.
    /// </summary>
    private sealed class CrossTenantFakeContext : ITenantContext
    {
        public static readonly CrossTenantFakeContext Instance = new();
        public long? TenantId => null;
        public long? UserId => null;
        public bool IsCrossTenantOperation => true;
    }
}
