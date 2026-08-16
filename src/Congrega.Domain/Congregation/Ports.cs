namespace Congrega.Domain.Congregation;

/// <summary>Filtro da listagem de membros.</summary>
public sealed record MemberQuery
{
    /// <summary>Busca por nome, e-mail ou telefone. Sem acento e sem diferenciar caixa.</summary>
    public string? Search { get; init; }

    public MemberStatus? Status { get; init; } = MemberStatus.Ativo;

    /// <summary>Aniversariantes do mês. 1 a 12.</summary>
    public int? BirthdayMonth { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>
    /// Teto obrigatório. Sem ele, <c>pageSize=100000</c> vira negação de serviço
    /// de graça — e o atacante nem precisa autenticar em outro tenant.
    /// </summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Linha da listagem. Projeção, não a entidade.</summary>
public sealed record MemberListItem
{
    public required Guid PublicId { get; init; }
    public required string FullName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public DateOnly? BirthDate { get; init; }
    public int? Age { get; init; }
    public required MemberStatus Status { get; init; }
    public string? FamilyName { get; init; }
}

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
}

public interface IMemberRepository
{
    /// <summary>
    /// Lista membros do tenant corrente.
    /// </summary>
    /// <remarks>
    /// O isolamento por tenant NÃO é parâmetro desta interface: vem do
    /// <c>ITenantContext</c>, aplicado pelo Global Query Filter e reforçado pelo
    /// RLS. Passar <c>tenantId</c> aqui convidaria algum handler a passar o
    /// errado, e a assinatura do método não deve permitir esse erro.
    /// </remarks>
    Task<PagedResult<MemberListItem>> ListAsync(MemberQuery query, CancellationToken cancellationToken);

    Task<Member?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>Conta membros ativos. Usado no painel e para limites de plano.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    void Add(Member member);
}
