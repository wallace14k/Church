using Congrega.Domain.Congregation;

namespace Congrega.Domain.Giving;

/// <summary>Filtro da listagem de lançamentos.</summary>
public sealed record GivingEntryQuery
{
    /// <summary>Ano e mês do período. Os dois juntos ou nenhum.</summary>
    public int? Year { get; init; }
    public int? Month { get; init; }

    public Guid? CategoryPublicId { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>Mesmo teto obrigatório da listagem de membros, e pelo mesmo motivo.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Linha da listagem de lançamentos. Projeção, não a entidade.</summary>
public sealed record GivingEntryListItem
{
    public required Guid PublicId { get; init; }
    public required string CategoryName { get; init; }
    public required GivingKind Kind { get; init; }
    public required long AmountCents { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required GivingMethod Method { get; init; }
    public string? MemberName { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Uma linha do fechamento — o total de uma categoria no período.</summary>
public sealed record ClosingLine
{
    public required Guid CategoryPublicId { get; init; }
    public required string CategoryName { get; init; }
    public required GivingKind Kind { get; init; }
    public required long TotalCents { get; init; }
    public required int EntryCount { get; init; }
}

/// <summary>
/// Fechamento de um mês.
/// </summary>
/// <remarks>
/// É um <b>relatório</b>, não um estado: nada é travado, nenhuma linha muda no
/// banco. Bloqueio de período com estorno é contabilidade de verdade e está na
/// Fase 2 (doc 05) — chamar de "fechamento" o que só soma é honesto enquanto a
/// tela não prometer que ninguém mais mexe no mês.
/// </remarks>
public sealed record MonthlyClosing
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required IReadOnlyList<ClosingLine> Lines { get; init; }

    public long TotalIncomeCents =>
        Lines.Where(l => l.Kind == GivingKind.Entrada).Sum(l => l.TotalCents);

    public long TotalExpenseCents =>
        Lines.Where(l => l.Kind == GivingKind.Saida).Sum(l => l.TotalCents);

    /// <summary>Entradas menos saídas. Pode ser negativo — e é uma informação, não um erro.</summary>
    public long BalanceCents => TotalIncomeCents - TotalExpenseCents;
}

public interface IGivingCategoryRepository
{
    /// <summary>
    /// Categorias do tenant corrente. Inativas entram só quando pedidas: o
    /// formulário de lançamento não deve oferecê-las, mas o filtro do relatório
    /// histórico precisa delas.
    /// </summary>
    Task<IReadOnlyList<GivingCategory>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<GivingCategory?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    void Add(GivingCategory category);
}

public interface IGivingEntryRepository
{
    Task<PagedResult<GivingEntryListItem>> ListAsync(
        GivingEntryQuery query,
        CancellationToken cancellationToken);

    Task<GivingEntry?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>
    /// Soma o mês agrupando por categoria.
    /// </summary>
    /// <remarks>
    /// A agregação acontece no banco. Trazer os lançamentos e somar em memória
    /// funcionaria com trinta linhas e derrubaria o processo com trinta mil —
    /// e o relatório de fechamento é justamente a consulta que cresce todo mês.
    /// </remarks>
    Task<MonthlyClosing> SummarizeMonthAsync(int year, int month, CancellationToken cancellationToken);

    void Add(GivingEntry entry);

    void Remove(GivingEntry entry);
}
