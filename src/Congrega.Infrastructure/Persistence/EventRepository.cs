using Congrega.Domain.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class EventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(e => e.PublicId).HasColumnName("public_id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id");
        builder.Property(e => e.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(e => e.StartsAt).HasColumnName("starts_at");
        builder.Property(e => e.EndsAt).HasColumnName("ends_at");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(e => e.Type).HasColumnName("event_type").HasConversion<short>();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.PublicId).IsUnique();

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class EventRepository(CongregaDbContext db) : IEventRepository
{
    public async Task<IReadOnlyList<CalendarEvent>> ListAsync(
        EventQuery query,
        CancellationToken cancellationToken)
    {
        // Sobreposição de intervalos: começa antes do fim da janela E termina
        // depois do início dela. Ver a nota em IEventRepository.ListAsync.
        IQueryable<CalendarEvent> source = db.Events
            .AsNoTracking()
            .Where(e => e.StartsAt < query.To && e.EndsAt > query.From);

        if (!query.IncludeCanceled)
        {
            source = source.Where(e => e.Status != EventStatus.Cancelado);
        }

        return await source
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Title)
            .ToListAsync(cancellationToken);
    }

    public Task<CalendarEvent?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.Events.FirstOrDefaultAsync(e => e.PublicId == publicId, cancellationToken);

    public async Task<IReadOnlyList<CalendarEvent>> ListUpcomingAsync(
        DateTimeOffset from,
        int limit,
        CancellationToken cancellationToken)
    {
        // Mesmo motivo do EventQuery: parâmetro com offset != 0 faz o Npgsql
        // recusar a consulta inteira.
        var desde = from.ToUniversalTime();

        return await db.Events
            .AsNoTracking()
            // Ainda não terminou, e não foi cancelado: "próximos" no painel
            // significa aquilo em que vale a pena aparecer. Um evento em curso
            // continua sendo o próximo compromisso de quem abre o app agora.
            .Where(e => e.EndsAt > desde && e.Status != EventStatus.Cancelado)
            .OrderBy(e => e.StartsAt)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);
    }

    public void Add(CalendarEvent calendarEvent) => db.Events.Add(calendarEvent);

    public void Remove(CalendarEvent calendarEvent) => db.Events.Remove(calendarEvent);
}
