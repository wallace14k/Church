using Congrega.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Constrói o <see cref="CongregaDbContext"/> para as ferramentas de linha de
/// comando do EF Core.
/// </summary>
/// <remarks>
/// <para>
/// O <c>dotnet ef</c> precisa instanciar o contexto sem subir a aplicação. Sem
/// esta fábrica ele tentaria usar o host da API, que exige chave de assinatura,
/// pepper de OTP e conexão válida — e gerar uma migration passaria a depender de
/// ter os segredos de produção à mão.
/// </para>
/// <para>
/// A string de conexão vem de <c>CONGREGA_MIGRATIONS_CONNECTION</c>, com um
/// padrão de desenvolvimento. Migrations são geradas a partir do <b>modelo</b>,
/// não do banco, então a conexão só é usada de fato ao aplicar.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CongregaDbContext>
{
    private const string ConexaoPadrao =
        "Host=localhost;Port=5432;Database=congrega;Username=congrega;Password=postgres";

    public CongregaDbContext CreateDbContext(string[] args)
    {
        string conexao =
            Environment.GetEnvironmentVariable("CONGREGA_MIGRATIONS_CONNECTION") ?? ConexaoPadrao;

        var opcoes = new DbContextOptionsBuilder<CongregaDbContext>()
            .UseNpgsql(conexao, npgsql => npgsql.MigrationsHistoryTable("__congrega_migrations"))
            .Options;

        // Contexto vazio em tempo de design: não há requisição, logo não há tenant.
        // Os Global Query Filters não afetam a geração do schema.
        return new CongregaDbContext(opcoes, new ContextoDeDesign(), TimeProvider.System);
    }

    private sealed class ContextoDeDesign : ITenantContext
    {
        public long? TenantId => null;
        public long? UserId => null;

        /// <summary>
        /// <c>true</c> para que a ferramenta enxergue o modelo inteiro, sem os
        /// filtros por tenant recortando entidades.
        /// </summary>
        public bool IsCrossTenantOperation => true;
    }
}
