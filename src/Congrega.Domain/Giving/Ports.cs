using Congrega.Domain.Congregation;

namespace Congrega.Domain.Giving;

/// <summary>Filtro da listagem de lançamentos.</summary>
public sealed record GivingEntryQuery
{
    /// <summary>Ano e mês do período. Os dois juntos ou nenhum.</summary>
    public int? Year { get; init; }
    public int? Month { get; init; }

    public Guid? CategoryPublicId { get; init; }

    /// <summary>Entrada ou saída. Nulo traz os dois — é o chip "Todos".</summary>
    public GivingKind? Kind { get; init; }

    /// <summary>Busca no título e nas observações. Sem acento e sem diferenciar caixa.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>Mesmo teto obrigatório da listagem de membros, e pelo mesmo motivo.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Linha da listagem de lançamentos. Projeção, não a entidade.</summary>
public sealed record GivingEntryListItem
{
    public required Guid PublicId { get; init; }

    /// <summary>
    /// Título do lançamento, quando houver.
    /// </summary>
    /// <remarks>
    /// Nulo nos lançamentos gravados antes de o campo existir. Quem desenha cai
    /// no nome da categoria — inventar um título a partir dela produziria a
    /// repetição ("Dízimo / Categoria: Dízimo") que o campo veio resolver.
    /// </remarks>
    public string? Description { get; init; }

    public required Guid CategoryPublicId { get; init; }
    public required string CategoryName { get; init; }

    /// <summary>Cor da categoria, ou nulo para usar a do sistema.</summary>
    public string? CategoryColorHex { get; init; }

    /// <summary>
    /// Entrada ou saída — do <b>lançamento</b>, não da categoria.
    /// </summary>
    /// <remarks>
    /// A distinção importa desde que a categoria pode ser <c>Ambos</c>: ler o
    /// sinal da categoria devolveria "ambos" para um lançamento que é
    /// definidamente uma coisa só.
    /// </remarks>
    public required GivingKind Kind { get; init; }

    public required long AmountCents { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required GivingMethod Method { get; init; }
    public string? MemberName { get; init; }
    public string? AccountName { get; init; }
    public required bool IsRecurring { get; init; }
    public GivingRecurrence? Recurrence { get; init; }

    /// <summary>Realizado ou previsto. Só o realizado soma no fechamento.</summary>
    public required GivingEntryStatus Status { get; init; }

    /// <summary>Identidade da série periódica, quando pertence a uma.</summary>
    public Guid? SeriesId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Contagens do mês para os chips de filtro.
/// </summary>
/// <remarks>
/// <b>Do período inteiro, não da página.</b> Contar no cliente sobre os 50 itens
/// carregados diria "2 entradas" num mês com 30 — e o número apareceria ao lado
/// de um filtro que devolve as 30.
/// </remarks>
public sealed record GivingEntrySummary
{
    public required int Total { get; init; }
    public required int Entradas { get; init; }
    public required int Saidas { get; init; }

    /// <summary>Quantos lançamentos por categoria, indexado pelo <c>public_id</c>.</summary>
    public required IReadOnlyDictionary<Guid, int> PorCategoria { get; init; }
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

    /// <summary>
    /// Entradas ainda PREVISTAS no mês — dinheiro que não se moveu.
    /// </summary>
    /// <remarks>
    /// <b>Fora de <see cref="TotalIncomeCents"/> de propósito, e exposto ao lado
    /// dele pelo mesmo motivo.</b> Somar previsto ao realizado faria o fechamento
    /// afirmar que o aluguel de setembro já foi pago. Escondê-lo por completo faz
    /// a tela mostrar R$ 0,00 ao lado de uma lista com um lançamento de R$ 1.200
    /// — e quem lê conclui que o sistema perdeu a conta.
    /// </remarks>
    public required long PlannedIncomeCents { get; init; }

    /// <summary>Saídas ainda previstas. Ver <see cref="PlannedIncomeCents"/>.</summary>
    public required long PlannedExpenseCents { get; init; }

    /// <summary>Há previsto no mês? Decide se a tela precisa explicar a diferença.</summary>
    public bool HasPlanned => PlannedIncomeCents > 0 || PlannedExpenseCents > 0;

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

public interface IFinancialAccountRepository
{
    /// <summary>
    /// Contas do tenant corrente, em ordem alfabética.
    /// </summary>
    /// <remarks>
    /// Inativas entram só quando pedidas: o formulário de lançamento não deve
    /// oferecê-las, mas a tela que as administra precisa mostrá-las — senão
    /// desativar uma a faria sumir do único lugar onde se reativa.
    /// </remarks>
    Task<IReadOnlyList<FinancialAccount>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<FinancialAccount?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    void Add(FinancialAccount account);
}

public interface IGivingEntryRepository
{
    /// <summary>
    /// Contagens do período para os chips de filtro.
    /// </summary>
    /// <remarks>
    /// Uma consulta agregada, não uma por chip: a alternativa seria uma
    /// varredura da tabela por chip para desenhar uma linha.
    /// </remarks>
    Task<GivingEntrySummary> GetSummaryAsync(int year, int month, CancellationToken cancellationToken);

    Task<PagedResult<GivingEntryListItem>> ListAsync(
        GivingEntryQuery query,
        CancellationToken cancellationToken);

    Task<GivingEntry?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>
    /// O lançamento inteiro, para a tela de detalhe.
    /// </summary>
    /// <remarks>
    /// Consulta própria, e não <c>FindByPublicIdAsync</c> mais navegações: o
    /// detalhe precisa de nome de categoria, de membro, de conta, de autor e do
    /// documento fiscal — cinco tabelas que a entidade não carrega e que, se
    /// carregasse, viriam junto em toda listagem.
    /// </remarks>
    Task<GivingEntryDetail?> GetDetailAsync(Guid publicId, CancellationToken cancellationToken);

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
