using Congrega.Domain.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.ToTable("event_types");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(t => t.PublicId).HasColumnName("public_id");
        builder.Property(t => t.TenantId).HasColumnName("tenant_id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(EventType.MaxNameLength).IsRequired();
        builder.Property(t => t.Icon).HasColumnName("icon").HasMaxLength(EventType.MaxIconLength).IsRequired();
        builder.Property(t => t.ColorHex).HasColumnName("color_hex").HasMaxLength(7).IsFixedLength();
        builder.Property(t => t.IsActive).HasColumnName("is_active");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(t => t.PublicId).IsUnique();

        builder.Ignore(t => t.DomainEvents);
    }
}

internal sealed class EventTypeRepository(CongregaDbContext db) : IEventTypeRepository
{
    public async Task<IReadOnlyList<EventType>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var consulta = db.EventTypes.AsQueryable();

        if (!includeInactive)
        {
            consulta = consulta.Where(t => t.IsActive);
        }

        return await consulta
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<EventType?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.EventTypes.FirstOrDefaultAsync(t => t.PublicId == publicId, cancellationToken);

    /// <summary>
    /// Contagem de uso, em uma consulta agregada só.
    /// </summary>
    /// <remarks>
    /// Cancelados entram na conta de propósito: um culto cancelado continua
    /// classificado como culto, e apagar o tipo o deixaria órfão do mesmo jeito.
    /// A pergunta que esta contagem responde é "o banco vai me deixar excluir",
    /// e para o banco a linha cancelada é uma referência como qualquer outra.
    /// </remarks>
    public async Task<IReadOnlyDictionary<long, int>> CountEventsByTypeAsync(
        CancellationToken cancellationToken)
    {
        var contagens = await db.Events
            .Where(e => e.TypeId != null)
            .GroupBy(e => e.TypeId!.Value)
            .Select(g => new { TypeId = g.Key, Total = g.Count() })
            .ToListAsync(cancellationToken);

        return contagens.ToDictionary(c => c.TypeId, c => c.Total);
    }

    public void Add(EventType type) => db.EventTypes.Add(type);

    public void Remove(EventType type) => db.EventTypes.Remove(type);
}
