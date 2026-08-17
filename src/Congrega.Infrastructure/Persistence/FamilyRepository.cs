using Congrega.Domain.Congregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable("families");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(f => f.PublicId).HasColumnName("public_id");
        builder.Property(f => f.TenantId).HasColumnName("tenant_id");
        builder.Property(f => f.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(f => f.PublicId).IsUnique();

        builder.Ignore(f => f.DomainEvents);
    }
}

internal sealed class FamilyRepository(CongregaDbContext db) : IFamilyRepository
{
    public async Task<IReadOnlyList<FamilySummary>> ListAsync(CancellationToken cancellationToken)
    {
        var familias = await db.Families
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new FamilySummary
            {
                PublicId = f.PublicId,
                Name = f.Name,
                MemberCount = db.Members.Count(m => m.FamilyId == f.Id),
            })
            .ToListAsync(cancellationToken);

        return familias;
    }

    public Task<Family?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.Families.FirstOrDefaultAsync(f => f.PublicId == publicId, cancellationToken);

    public Task<string?> FindNameByIdAsync(long familyId, CancellationToken cancellationToken) =>
        db.Families
            .Where(f => f.Id == familyId)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<FamilyMemberItem>> ListMembersAsync(
        long familyId,
        CancellationToken cancellationToken) =>
        await db.Members
            .AsNoTracking()
            .Where(m => m.FamilyId == familyId)
            .OrderBy(m => m.FullName)
            .Select(m => new FamilyMemberItem
            {
                PublicId = m.PublicId,
                FullName = m.FullName,
                Status = m.Status,
            })
            .ToListAsync(cancellationToken);

    public void Add(Family family) => db.Families.Add(family);
}
