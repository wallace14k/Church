using Congrega.Domain.Congregation;
using Congrega.Domain.Giving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class GivingCategoryConfiguration : IEntityTypeConfiguration<GivingCategory>
{
    public void Configure(EntityTypeBuilder<GivingCategory> builder)
    {
        builder.ToTable("giving_categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(c => c.PublicId).HasColumnName("public_id");
        builder.Property(c => c.TenantId).HasColumnName("tenant_id");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(c => c.IsActive).HasColumnName("is_active");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.PublicId).IsUnique();

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class GivingEntryConfiguration : IEntityTypeConfiguration<GivingEntry>
{
    public void Configure(EntityTypeBuilder<GivingEntry> builder)
    {
        builder.ToTable("giving_entries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(e => e.PublicId).HasColumnName("public_id");
        builder.Property(e => e.TenantId).HasColumnName("tenant_id");
        builder.Property(e => e.CategoryId).HasColumnName("category_id");
        builder.Property(e => e.MemberId).HasColumnName("member_id");
        builder.Property(e => e.AmountCents).HasColumnName("amount_cents");
        builder.Property(e => e.OccurredOn).HasColumnName("occurred_on");
        builder.Property(e => e.Method).HasColumnName("method").HasConversion<short>();
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.RecordedByUserId).HasColumnName("recorded_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.PublicId).IsUnique();

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class GivingCategoryRepository(CongregaDbContext db) : IGivingCategoryRepository
{
    public async Task<IReadOnlyList<GivingCategory>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        IQueryable<GivingCategory> source = db.GivingCategories.AsNoTracking();

        if (!includeInactive)
        {
            source = source.Where(c => c.IsActive);
        }

        // Entradas antes de saídas, e alfabético dentro de cada grupo: é a
        // ordem em que o tesoureiro lê o fechamento, e a mesma do formulário.
        return await source
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<GivingCategory?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.GivingCategories.FirstOrDefaultAsync(c => c.PublicId == publicId, cancellationToken);

    public void Add(GivingCategory category) => db.GivingCategories.Add(category);
}

internal sealed class GivingEntryRepository(CongregaDbContext db) : IGivingEntryRepository
{
    public async Task<PagedResult<GivingEntryListItem>> ListAsync(
        GivingEntryQuery query,
        CancellationToken cancellationToken)
    {
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int page = Math.Max(query.Page, 1);

        IQueryable<GivingEntry> source = db.GivingEntries.AsNoTracking();

        if (query.Year is { } ano && query.Month is { } mes)
        {
            source = source.Where(e => e.OccurredOn.Year == ano && e.OccurredOn.Month == mes);
        }

        if (query.CategoryPublicId is { } categoriaPublica)
        {
            source = source.Where(e =>
                db.GivingCategories
                    .Where(c => c.PublicId == categoriaPublica)
                    .Select(c => c.Id)
                    .Contains(e.CategoryId));
        }

        int total = await source.CountAsync(cancellationToken);

        // Mais recente primeiro: quem abre a lista quer conferir o que acabou de
        // lançar, não o primeiro lançamento do mês.
        var itens = await source
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new GivingEntryListItem
            {
                PublicId = e.PublicId,
                CategoryName = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault()!,
                Kind = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId)
                    .Select(c => c.Kind)
                    .FirstOrDefault(),
                AmountCents = e.AmountCents,
                OccurredOn = e.OccurredOn,
                Method = e.Method,
                MemberName = db.Members
                    .Where(m => m.Id == e.MemberId)
                    .Select(m => m.FullName)
                    .FirstOrDefault(),
                Notes = e.Notes,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<GivingEntryListItem>
        {
            Items = itens,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    public Task<GivingEntry?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.GivingEntries.FirstOrDefaultAsync(e => e.PublicId == publicId, cancellationToken);

    public async Task<MonthlyClosing> SummarizeMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        // GroupBy traduzido para SQL — a soma acontece no banco. Ver a nota em
        // IGivingEntryRepository.SummarizeMonthAsync.
        var linhas = await db.GivingEntries
            .AsNoTracking()
            .Where(e => e.OccurredOn.Year == year && e.OccurredOn.Month == month)
            .GroupBy(e => e.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                TotalCents = g.Sum(e => e.AmountCents),
                EntryCount = g.Count(),
            })
            .Join(
                db.GivingCategories.AsNoTracking(),
                agregado => agregado.CategoryId,
                categoria => categoria.Id,
                (agregado, categoria) => new ClosingLine
                {
                    CategoryPublicId = categoria.PublicId,
                    CategoryName = categoria.Name,
                    Kind = categoria.Kind,
                    TotalCents = agregado.TotalCents,
                    EntryCount = agregado.EntryCount,
                })
            .OrderBy(l => l.Kind)
            .ThenByDescending(l => l.TotalCents)
            .ToListAsync(cancellationToken);

        return new MonthlyClosing { Year = year, Month = month, Lines = linhas };
    }

    public void Add(GivingEntry entry) => db.GivingEntries.Add(entry);

    public void Remove(GivingEntry entry) => db.GivingEntries.Remove(entry);
}
