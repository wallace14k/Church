namespace Congrega.Domain.Congregation;

/// <summary>Filtro da listagem de membros.</summary>
/// <summary>
/// Que lacuna do cadastro filtrar.
/// </summary>
/// <remarks>
/// A secretaria usa isto para saber de quem ainda falta dado — "quem eu preciso
/// cobrar". É pergunta de trabalho, não de relatório, e por isso vira filtro da
/// própria listagem em vez de tela separada.
/// </remarks>
public enum MemberGap
{
    /// <summary>Falta telefone OU e-mail.</summary>
    Any = 1,
    SemTelefone = 2,
    SemEmail = 3,
}

/// <summary>
/// Contagens do acervo inteiro, para os chips de filtro.
/// </summary>
/// <remarks>
/// <b>Do acervo, não da página.</b> Contar no cliente sobre os 50 itens
/// carregados diria "5 incompletos" numa igreja com 9 — e o número apareceria
/// ao lado de um filtro que devolve os 9. Um chip que mente sobre o que vai
/// mostrar é pior do que chip nenhum.
/// </remarks>
public sealed record MemberSummary
{
    public required int Total { get; init; }
    public required int BirthdayThisMonth { get; init; }
    /// <summary>Sem telefone ou sem e-mail.</summary>
    public required int Incomplete { get; init; }
    public required int WithoutPhone { get; init; }
    public required int WithoutEmail { get; init; }
}

public sealed record MemberQuery
{
    /// <summary>Busca por nome, e-mail ou telefone. Sem acento e sem diferenciar caixa.</summary>
    public string? Search { get; init; }

    public MemberStatus? Status { get; init; } = MemberStatus.Ativo;

    /// <summary>Aniversariantes do mês. 1 a 12.</summary>
    public int? BirthdayMonth { get; init; }

    /// <summary>Filtra por lacuna do cadastro. Nulo traz todos.</summary>
    public MemberGap? Gap { get; init; }

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

    /// <summary>
    /// Contagens do acervo para os chips de filtro.
    /// </summary>
    /// <remarks>
    /// <b>Uma consulta agregada, não cinco.</b> Cinco <c>COUNT</c> separados
    /// seriam cinco varreduras da mesma tabela para desenhar uma linha de
    /// chips; somar condições numa projeção só responde tudo numa passada.
    ///
    /// <para>
    /// Respeita o mesmo <c>status</c> da listagem: se a tela mostra ativos, os
    /// chips contam ativos. Divergir faria o chip prometer 12 e a lista
    /// entregar 9.
    /// </para>
    /// </remarks>
    Task<MemberSummary> GetSummaryAsync(
        MemberStatus? status,
        int birthdayMonth,
        CancellationToken cancellationToken);

    Task<Member?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>Conta membros ativos. Usado no painel e para limites de plano.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// E-mails já cadastrados no tenant corrente, em minúsculas.
    /// </summary>
    /// <remarks>
    /// Existe para a importação em lote: checar duplicidade linha a linha faria
    /// uma consulta por linha da planilha. Uma consulta só, comparada em memória
    /// contra as poucas centenas de linhas de um lote, resolve sem N+1.
    /// </remarks>
    Task<IReadOnlySet<string>> ListEmailsAsync(CancellationToken cancellationToken);

    void Add(Member member);
}

/// <summary>Linha da listagem de famílias — inclui quantos membros já apontam para ela.</summary>
public sealed record FamilySummary
{
    public required Guid PublicId { get; init; }
    public required string Name { get; init; }
    public required int MemberCount { get; init; }
}

/// <summary>Um membro dentro da ficha de família — o suficiente para a lista, não a ficha inteira.</summary>
public sealed record FamilyMemberItem
{
    public required Guid PublicId { get; init; }
    public required string FullName { get; init; }
    public required MemberStatus Status { get; init; }
}

public interface IFamilyRepository
{
    /// <summary>Lista famílias do tenant corrente, com a contagem de membros de cada uma.</summary>
    Task<IReadOnlyList<FamilySummary>> ListAsync(CancellationToken cancellationToken);

    Task<Family?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>
    /// Nome de uma família a partir do id interno — o suficiente para enriquecer a
    /// ficha de um membro sem carregar o agregado inteiro.
    /// </summary>
    Task<string?> FindNameByIdAsync(long familyId, CancellationToken cancellationToken);

    /// <summary>Membros que apontam para esta família — para a ficha de família.</summary>
    Task<IReadOnlyList<FamilyMemberItem>> ListMembersAsync(long familyId, CancellationToken cancellationToken);

    void Add(Family family);
}
