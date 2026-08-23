using Congrega.Domain.Giving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Congrega.Infrastructure.Persistence;

internal sealed class FiscalDocumentConfiguration : IEntityTypeConfiguration<FiscalDocument>
{
    public void Configure(EntityTypeBuilder<FiscalDocument> builder)
    {
        builder.ToTable("giving_documents");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(d => d.PublicId).HasColumnName("public_id");
        builder.Property(d => d.TenantId).HasColumnName("tenant_id");
        builder.Property(d => d.EntryId).HasColumnName("entry_id");
        builder.Property(d => d.EntryKind).HasColumnName("entry_kind").HasConversion<short>();
        builder.Property(d => d.DocumentType).HasColumnName("doc_type").HasConversion<short>();
        builder.Property(d => d.Number).HasColumnName("number")
            .HasMaxLength(FiscalDocument.MaxNumberLength);
        builder.Property(d => d.Series).HasColumnName("series")
            .HasMaxLength(FiscalDocument.MaxSeriesLength);
        builder.Property(d => d.IssuerTaxId).HasColumnName("issuer_tax_id").HasMaxLength(14);
        builder.Property(d => d.AccessKey).HasColumnName("access_key")
            .HasMaxLength(FiscalDocument.AccessKeyLength);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(d => d.PublicId).IsUnique();
        builder.HasIndex(d => d.EntryId).IsUnique();

        builder.Ignore(d => d.DomainEvents);
    }
}

internal sealed class FiscalDocumentFileConfiguration : IEntityTypeConfiguration<FiscalDocumentFile>
{
    public void Configure(EntityTypeBuilder<FiscalDocumentFile> builder)
    {
        builder.ToTable("giving_document_files");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(f => f.PublicId).HasColumnName("public_id");
        builder.Property(f => f.TenantId).HasColumnName("tenant_id");
        builder.Property(f => f.DocumentId).HasColumnName("document_id");
        builder.Property(f => f.FileName).HasColumnName("file_name")
            .HasMaxLength(FiscalDocumentFile.MaxFileNameLength).IsRequired();
        builder.Property(f => f.ContentType).HasColumnName("content_type")
            .HasMaxLength(100).IsRequired();
        builder.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        builder.Property(f => f.Content).HasColumnName("content").IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(f => f.PublicId).IsUnique();
        builder.HasIndex(f => f.DocumentId).IsUnique();

        builder.Ignore(f => f.DomainEvents);
    }
}

internal sealed class FiscalDocumentRepository(CongregaDbContext db) : IFiscalDocumentRepository
{
    public Task<FiscalDocument?> FindByEntryIdAsync(long entryId, CancellationToken cancellationToken) =>
        db.FiscalDocuments.FirstOrDefaultAsync(d => d.EntryId == entryId, cancellationToken);

    /// <summary>
    /// O arquivo, resolvido a partir do lançamento.
    /// </summary>
    /// <remarks>
    /// <b>Esta é a única consulta que traz os bytes</b>, e é por isso que ela
    /// existe separada de tudo o mais: qualquer projeção que incluísse
    /// <c>Content</c> por engano faria a listagem do mês transferir megabytes
    /// para desenhar uma coluna de valores.
    /// </remarks>
    public async Task<FiscalDocumentFileContent?> FindFileByEntryPublicIdAsync(
        Guid entryPublicId,
        CancellationToken cancellationToken)
    {
        return await (
            from lancamento in db.GivingEntries.AsNoTracking()
            where lancamento.PublicId == entryPublicId
            join documento in db.FiscalDocuments.AsNoTracking()
                on lancamento.Id equals documento.EntryId
            join arquivo in db.FiscalDocumentFiles.AsNoTracking()
                on documento.Id equals arquivo.DocumentId
            select new FiscalDocumentFileContent
            {
                FileName = arquivo.FileName,
                ContentType = arquivo.ContentType,
                Content = arquivo.Content,
            }).FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(FiscalDocument document) => db.FiscalDocuments.Add(document);

    public void AddFile(FiscalDocumentFile file) => db.FiscalDocumentFiles.Add(file);
}
