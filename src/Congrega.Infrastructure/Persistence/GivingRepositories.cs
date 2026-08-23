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
        builder.Property(c => c.ColorHex).HasColumnName("color_hex").HasMaxLength(7).IsFixedLength();
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
        builder.Property(e => e.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(e => e.Description).HasColumnName("description")
            .HasMaxLength(GivingEntry.MaxDescriptionLength);
        builder.Property(e => e.AccountId).HasColumnName("account_id");
        builder.Property(e => e.IsRecurring).HasColumnName("is_recurring");
        builder.Property(e => e.Recurrence).HasColumnName("recurrence").HasConversion<short?>();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(e => e.SeriesId).HasColumnName("series_id");
        builder.Property(e => e.MemberId).HasColumnName("member_id");
        builder.Property(e => e.AmountCents).HasColumnName("amount_cents");
        builder.Property(e => e.OccurredOn).HasColumnName("occurred_on");
        builder.Property(e => e.Method).HasColumnName("method").HasConversion<short>();
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(GivingEntry.MaxNotesLength);
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

        // O tipo vem do LANÇAMENTO, não da categoria: filtrar pela categoria
        // deixaria de fora todo lançamento feito numa categoria `Ambos`.
        if (query.Kind is { } tipo)
        {
            source = source.Where(e => e.Kind == tipo);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string termo = query.Search.Trim();

            // Normaliza os DOIS lados, como na busca de membros: sem isso
            // "aluguel" não encontra "Aluguél" nem "ALUGUEL".
            string alvo = NormalizacaoDeBusca.RemoverAcentos(termo).ToLowerInvariant();

            source = source.Where(e =>
                (e.Description != null &&
                 EF.Functions.ILike(CongregaDbContext.Unaccent(e.Description), $"%{alvo}%")) ||
                (e.Notes != null && EF.Functions.ILike(CongregaDbContext.Unaccent(e.Notes), $"%{alvo}%")));
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
                Description = e.Description,
                CategoryPublicId = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId)
                    .Select(c => c.PublicId)
                    .FirstOrDefault(),
                CategoryName = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault()!,
                CategoryColorHex = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId)
                    .Select(c => c.ColorHex)
                    .FirstOrDefault(),
                // Do lançamento, e não mais da categoria: a categoria pode ser
                // `Ambos`, e a linha precisa dizer o que ELA é.
                Kind = e.Kind,
                AmountCents = e.AmountCents,
                OccurredOn = e.OccurredOn,
                Method = e.Method,
                MemberName = db.Members
                    .Where(m => m.Id == e.MemberId)
                    .Select(m => m.FullName)
                    .FirstOrDefault(),
                AccountName = db.FinancialAccounts
                    .Where(a => a.Id == e.AccountId)
                    .Select(a => a.Name)
                    .FirstOrDefault(),
                IsRecurring = e.IsRecurring,
                Recurrence = e.Recurrence,
                Status = e.Status,
                SeriesId = e.SeriesId,
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

    /// <summary>
    /// Contagens do mês para os chips de filtro.
    /// </summary>
    /// <remarks>
    /// Duas consultas: uma agregada para os totais, outra agrupada por
    /// categoria. Não é possível fundi-las sem trazer uma linha por categoria
    /// com o total repetido — e as duas juntas ainda são muito menos do que uma
    /// varredura por chip.
    /// </remarks>
    public async Task<GivingEntrySummary> GetSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        // O resumo conta o que a listagem mostra, e a listagem mostra os dois
        // estados: quem abre o mês quer ver o que já aconteceu E o que está
        // previsto. Quem separa os dois é o chip, não a consulta.
        IQueryable<GivingEntry> doMes = db.GivingEntries
            .AsNoTracking()
            .Where(e => e.OccurredOn.Year == year && e.OccurredOn.Month == month);

        var totais = await doMes
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Entradas = g.Count(e => e.Kind == GivingKind.Entrada),
                Saidas = g.Count(e => e.Kind == GivingKind.Saida),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var porCategoria = await doMes
            .GroupBy(e => e.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Count() })
            .Join(
                db.GivingCategories.AsNoTracking(),
                linha => linha.CategoryId,
                categoria => categoria.Id,
                (linha, categoria) => new { categoria.PublicId, linha.Total })
            .ToListAsync(cancellationToken);

        // Mês sem lançamento: o `GroupBy` não produz linha, e sem este caminho o
        // resumo viria nulo em vez de zeros.
        return new GivingEntrySummary
        {
            Total = totais?.Total ?? 0,
            Entradas = totais?.Entradas ?? 0,
            Saidas = totais?.Saidas ?? 0,
            PorCategoria = porCategoria.ToDictionary(c => c.PublicId, c => c.Total),
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
        //
        // **Agrupa por (categoria, tipo do LANÇAMENTO)**, e não só por categoria.
        //
        // Agrupar só por categoria e ler o sinal dela era um bug real, com dado
        // real: uma categoria `Ambos` produzia uma linha com `Kind = Ambos`, e
        // `MonthlyClosing` soma `Entrada` em receita e `Saida` em despesa — a
        // linha não entrava em nenhuma das duas. R$ 84,00 de saídas ficaram
        // invisíveis no fechamento de agosto até isto ser corrigido.
        //
        // Uma categoria `Ambos` com entradas E saídas no mesmo mês agora produz
        // duas linhas, que é o que ela de fato é.
        //
        // **Só o realizado soma.** O previsto — as parcelas futuras de uma série
        // periódica — existe para planejar, e contá-lo faria o caixa afirmar que
        // dinheiro que não se moveu já se moveu.
        var linhas = await db.GivingEntries
            .AsNoTracking()
            .Where(e =>
                e.OccurredOn.Year == year
                && e.OccurredOn.Month == month
                && e.Status == GivingEntryStatus.Realizado)
            .GroupBy(e => new { e.CategoryId, e.Kind })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Kind,
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
                    Kind = agregado.Kind,
                    TotalCents = agregado.TotalCents,
                    EntryCount = agregado.EntryCount,
                })
            .OrderBy(l => l.Kind)
            .ThenByDescending(l => l.TotalCents)
            .ToListAsync(cancellationToken);

        // Os previstos do mês, somados por tipo.
        //
        // **Consulta separada, e não um GroupBy a mais na de cima.** Incluir o
        // status no agrupamento dobraria as linhas por categoria, e são elas que
        // desenham o resumo do mês — o painel passaria a mostrar "Aluguel" duas
        // vezes, uma realizada e outra prevista, sem dizer qual é qual. Aqui o
        // previsto é um total, não um detalhamento.
        var previstos = await db.GivingEntries
            .AsNoTracking()
            .Where(e =>
                e.OccurredOn.Year == year
                && e.OccurredOn.Month == month
                && e.Status == GivingEntryStatus.Previsto)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, TotalCents = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);

        return new MonthlyClosing
        {
            Year = year,
            Month = month,
            Lines = linhas,
            PlannedIncomeCents = previstos
                .Where(p => p.Kind == GivingKind.Entrada).Sum(p => p.TotalCents),
            PlannedExpenseCents = previstos
                .Where(p => p.Kind == GivingKind.Saida).Sum(p => p.TotalCents),
        };
    }

    /// <summary>
    /// O lançamento inteiro, numa consulta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As subconsultas correlacionadas (categoria, membro, conta, autor,
    /// documento) viram <c>LEFT JOIN LATERAL</c> no Postgres e trazem tudo numa
    /// ida só. Carregar as entidades e navegar produziria o mesmo resultado com
    /// seis viagens ao banco.
    /// </para>
    /// <para>
    /// <b>O anexo entra sem os bytes.</b> Só `fileName`, `contentType` e
    /// `sizeBytes` — o suficiente para a tela dizer "nota.pdf · 240 KB". Quem
    /// quiser ver o arquivo pede em outra chamada, que é a única que paga o
    /// custo dele.
    /// </para>
    /// </remarks>
    public async Task<GivingEntryDetail?> GetDetailAsync(
        Guid publicId,
        CancellationToken cancellationToken)
    {
        return await db.GivingEntries
            .AsNoTracking()
            .Where(e => e.PublicId == publicId)
            .Select(e => new GivingEntryDetail
            {
                PublicId = e.PublicId,
                Description = e.Description,
                CategoryPublicId = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId).Select(c => c.PublicId).FirstOrDefault(),
                CategoryName = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId).Select(c => c.Name).FirstOrDefault()!,
                CategoryColorHex = db.GivingCategories
                    .Where(c => c.Id == e.CategoryId).Select(c => c.ColorHex).FirstOrDefault(),
                Kind = e.Kind,
                AmountCents = e.AmountCents,
                OccurredOn = e.OccurredOn,
                Method = e.Method,
                MemberName = db.Members
                    .Where(m => m.Id == e.MemberId).Select(m => m.FullName).FirstOrDefault(),
                AccountName = db.FinancialAccounts
                    .Where(c => c.Id == e.AccountId).Select(c => c.Name).FirstOrDefault(),
                IsRecurring = e.IsRecurring,
                Recurrence = e.Recurrence,
                Status = e.Status,
                SeriesId = e.SeriesId,
                Notes = e.Notes,

                // `IgnoreQueryFilters` porque `users` não tem `tenant_id`: a
                // identidade é global neste sistema e o filtro de tenant não se
                // aplica a ela.
                RecordedByName = db.Users.IgnoreQueryFilters()
                    .Where(u => u.Id == e.RecordedByUserId).Select(u => u.FullName).FirstOrDefault(),

                CreatedAt = e.CreatedAt,

                Document = db.FiscalDocuments
                    .Where(d => d.EntryId == e.Id)
                    .Select(d => new FiscalDocumentView
                    {
                        PublicId = d.PublicId,
                        DocumentType = d.DocumentType,
                        Number = d.Number,
                        Series = d.Series,
                        IssuerTaxId = d.IssuerTaxId,
                        AccessKey = d.AccessKey,
                        File = db.FiscalDocumentFiles
                            .Where(f => f.DocumentId == d.Id)
                            .Select(f => new FiscalDocumentFileInfo
                            {
                                PublicId = f.PublicId,
                                FileName = f.FileName,
                                ContentType = f.ContentType,
                                SizeBytes = f.SizeBytes,
                            })
                            .FirstOrDefault(),
                    })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(GivingEntry entry) => db.GivingEntries.Add(entry);

    public void Remove(GivingEntry entry) => db.GivingEntries.Remove(entry);
}

internal sealed class FinancialAccountConfiguration : IEntityTypeConfiguration<FinancialAccount>
{
    public void Configure(EntityTypeBuilder<FinancialAccount> builder)
    {
        builder.ToTable("financial_accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(a => a.PublicId).HasColumnName("public_id");
        builder.Property(a => a.TenantId).HasColumnName("tenant_id");
        builder.Property(a => a.Name).HasColumnName("name")
            .HasMaxLength(FinancialAccount.MaxNameLength).IsRequired();
        builder.Property(a => a.Kind).HasColumnName("kind").HasConversion<short>();
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => a.PublicId).IsUnique();

        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class FinancialAccountRepository(CongregaDbContext db) : IFinancialAccountRepository
{
    public async Task<IReadOnlyList<FinancialAccount>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var consulta = db.FinancialAccounts.AsQueryable();

        if (!includeInactive)
        {
            consulta = consulta.Where(a => a.IsActive);
        }

        return await consulta.OrderBy(a => a.Name).ToListAsync(cancellationToken);
    }

    public Task<FinancialAccount?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken) =>
        db.FinancialAccounts.FirstOrDefaultAsync(a => a.PublicId == publicId, cancellationToken);

    public void Add(FinancialAccount account) => db.FinancialAccounts.Add(account);
}
