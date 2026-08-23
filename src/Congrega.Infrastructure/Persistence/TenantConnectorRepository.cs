using System.Text.Json;
using Congrega.Domain.Connectors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class TenantConnectorConfiguration : IEntityTypeConfiguration<TenantConnector>
{
    /// <summary>
    /// Comparador do dicionário de configuração.
    /// </summary>
    /// <remarks>
    /// <b>Sem ele o EF não detecta mudança dentro do dicionário.</b> A referência
    /// continua a mesma quando uma chave muda de valor, e o <c>SaveChanges</c>
    /// não gera <c>UPDATE</c> — a porta do SMTP mudaria na tela e não no banco.
    /// Comparar pelo conteúdo é o que torna a edição visível ao rastreador.
    /// </remarks>
    private static readonly ValueComparer<IReadOnlyDictionary<string, string>> Comparador =
        new(
            (a, b) => a != null && b != null && a.Count == b.Count
                && a.All(par => b.ContainsKey(par.Key) && b[par.Key] == par.Value),
            d => d.Aggregate(0, (acumulado, par) =>
                HashCode.Combine(acumulado, par.Key.GetHashCode(StringComparison.Ordinal),
                    par.Value.GetHashCode(StringComparison.Ordinal))),
            d => new Dictionary<string, string>(d, StringComparer.Ordinal));

    public void Configure(EntityTypeBuilder<TenantConnector> builder)
    {
        builder.ToTable("tenant_connectors");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(c => c.PublicId).HasColumnName("public_id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id");
        builder.Property(c => c.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(c => c.IsEnabled).HasColumnName("is_enabled");
        builder.Property(c => c.SecretCiphertext).HasColumnName("secret_enc");
        builder.Property(c => c.LastTestedAt).HasColumnName("last_tested_at");
        builder.Property(c => c.LastTestSucceeded).HasColumnName("last_test_succeeded");
        builder.Property(c => c.LastTestMessage).HasColumnName("last_test_message")
            .HasMaxLength(TenantConnector.MaxTestMessageLength);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // O domínio expõe um dicionário e não conhece JSON — é a persistência
        // que traduz. `Congrega.Domain` não tem PackageReference nenhuma, e uma
        // regra de negócio que soubesse serializar já não seria regra de
        // negócio.
        builder.Property(c => c.Settings)
            .HasColumnName("settings")
            .HasColumnType("jsonb")
            .HasConversion(
                valor => JsonSerializer.Serialize(valor, JsonPadrao),
                texto => Desserializar(texto))
            .Metadata.SetValueComparer(Comparador);

        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.Kind }).IsUnique();

        builder.Ignore(c => c.DomainEvents);
    }

    private static readonly JsonSerializerOptions JsonPadrao = new(JsonSerializerDefaults.Web);

    private static Dictionary<string, string> Desserializar(string texto) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(texto, JsonPadrao)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed class TenantConnectorRepository(CongregaDbContext db) : ITenantConnectorRepository
{
    public async Task<IReadOnlyList<TenantConnector>> ListAsync(CancellationToken cancellationToken) =>
        await db.TenantConnectors.OrderBy(c => c.Kind).ToListAsync(cancellationToken);

    public Task<TenantConnector?> FindByKindAsync(
        ConnectorKind kind,
        CancellationToken cancellationToken) =>
        db.TenantConnectors.FirstOrDefaultAsync(c => c.Kind == kind, cancellationToken);

    public void Add(TenantConnector connector) => db.TenantConnectors.Add(connector);

    public void Remove(TenantConnector connector) => db.TenantConnectors.Remove(connector);
}
