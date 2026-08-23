using Congrega.Domain.Addressing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class PostalCodeConfiguration : IEntityTypeConfiguration<PostalCode>
{
    public void Configure(EntityTypeBuilder<PostalCode> builder)
    {
        builder.ToTable("postal_codes");
        builder.HasKey(p => p.Cep);

        builder.Property(p => p.Cep).HasColumnName("cep").HasMaxLength(8).IsFixedLength();
        builder.Property(p => p.Logradouro).HasColumnName("logradouro").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Bairro).HasColumnName("bairro").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Localidade).HasColumnName("localidade").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Estado).HasColumnName("estado").HasMaxLength(60).IsRequired();
        builder.Property(p => p.FetchedAt).HasColumnName("fetched_at");
    }
}

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("addresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(a => a.PublicId).HasColumnName("public_id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.Cep).HasColumnName("cep").HasMaxLength(8).IsFixedLength();
        builder.Property(a => a.Logradouro).HasColumnName("logradouro").HasMaxLength(200);
        builder.Property(a => a.Bairro).HasColumnName("bairro").HasMaxLength(100);
        builder.Property(a => a.Localidade).HasColumnName("localidade").HasMaxLength(100);
        builder.Property(a => a.Estado).HasColumnName("estado").HasMaxLength(60);
        builder.Property(a => a.ResidenceType).HasColumnName("residence_type").HasConversion<short>();
        builder.Property(a => a.Numero).HasColumnName("numero").HasMaxLength(Address.MaxNumeroLength);
        builder.Property(a => a.Andar).HasColumnName("andar").HasMaxLength(Address.MaxAndarLength);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => a.PublicId).IsUnique();

        builder.Ignore(a => a.IsEmpty);
        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class AddressRepository(CongregaDbContext db) : IAddressRepository
{
    public Task<Address?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.Addresses.FirstOrDefaultAsync(a => a.PublicId == publicId, cancellationToken);

    public Task<Address?> FindByIdAsync(long id, CancellationToken cancellationToken) =>
        db.Addresses.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<long, Address>> ListByIdsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            // Evita um `WHERE id = ANY('{}')` inútil no banco a cada listagem
            // sem endereço nenhum, que é o caso comum numa igreja começando.
            return new Dictionary<long, Address>();
        }

        var itens = await db.Addresses
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(cancellationToken);

        return itens.ToDictionary(a => a.Id);
    }

    public void Add(Address address) => db.Addresses.Add(address);

    public void Remove(Address address) => db.Addresses.Remove(address);
}

/// <summary>
/// Cache de CEP. Global — nenhuma consulta aqui é filtrada por tenant.
/// </summary>
internal sealed class PostalCodeRepository(CongregaDbContext db) : IPostalCodeRepository
{
    public Task<PostalCode?> FindAsync(string cepNormalizado, CancellationToken cancellationToken) =>
        db.PostalCodes.FirstOrDefaultAsync(p => p.Cep == cepNormalizado, cancellationToken);

    /// <summary>
    /// Grava o CEP, tolerando que outra requisição tenha gravado antes.
    /// </summary>
    /// <remarks>
    /// Duas pessoas digitando o mesmo CEP ao mesmo tempo consultam a ViaCEP em
    /// paralelo e tentam gravar as duas. A segunda perde para a chave primária,
    /// e perder está certo — o conteúdo é idêntico. Engolir a violação aqui é
    /// mais barato do que serializar o cache, e não há nada a corrigir: o dado
    /// que interessa já está lá.
    /// </remarks>
    public async Task SaveAsync(PostalCode postalCode, CancellationToken cancellationToken)
    {
        db.PostalCodes.Add(postalCode);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Domain.Common.UniqueConstraintViolationException)
        {
            // Desanexa para o `SaveChanges` seguinte — o do cadastro em si — não
            // tentar inserir de novo a linha que já falhou.
            db.Entry(postalCode).State = EntityState.Detached;
        }
    }
}
