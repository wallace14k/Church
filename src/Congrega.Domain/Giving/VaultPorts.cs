using Congrega.Domain.Congregation;

namespace Congrega.Domain.Giving;

/// <summary>
/// O estado atual do cofre: quanto tem, e onde parou a numeração.
/// </summary>
/// <remarks>
/// Os dois juntos, numa leitura só, porque é assim que são usados: registrar um
/// movimento precisa do saldo <b>e</b> da sequência, e buscá-los em duas
/// consultas abriria a janela de eles virem de estados diferentes do cofre.
/// </remarks>
public sealed record VaultState
{
    public required long BalanceCents { get; init; }

    /// <summary>Número do último movimento. <c>0</c> num cofre nunca usado.</summary>
    public required long LastSequence { get; init; }

    /// <summary>Quantos movimentos o cofre já teve.</summary>
    public required int MovementCount { get; init; }
}

/// <summary>Uma linha do extrato do cofre.</summary>
public sealed record VaultMovementListItem
{
    public required Guid PublicId { get; init; }
    public required long SequenceNumber { get; init; }
    public required VaultDirection Direction { get; init; }
    public required long AmountCents { get; init; }
    public required long BalanceAfterCents { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? AccountName { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// Quem fez o movimento.
    /// </summary>
    /// <remarks>
    /// Nome, e não id: o extrato do cofre é lido por gente conferindo dinheiro
    /// em espécie, e "usuário 47" não responde a pergunta que essa pessoa está
    /// fazendo.
    /// </remarks>
    public string? PerformedByName { get; init; }
}

public interface IVaultRepository
{
    /// <summary>
    /// Saldo e última sequência do cofre do tenant corrente.
    /// </summary>
    /// <remarks>
    /// Lê o <b>último movimento</b>, e não uma soma de tudo: o saldo já está
    /// gravado nele. Somar a tabela inteira a cada operação transformaria o
    /// cofre numa consulta que fica mais lenta a cada depósito.
    /// </remarks>
    Task<VaultState> GetStateAsync(CancellationToken cancellationToken);

    Task<PagedResult<VaultMovementListItem>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void Add(VaultMovement movement);
}
