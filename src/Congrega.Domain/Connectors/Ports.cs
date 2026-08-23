namespace Congrega.Domain.Connectors;

public interface ITenantConnectorRepository
{
    /// <summary>Todos os conectores da igreja corrente.</summary>
    Task<IReadOnlyList<TenantConnector>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// O conector de um tipo, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Busca por <b>tipo</b>, e não por id: existe no máximo um SMTP por igreja
    /// — é o índice <c>UNIQUE (tenant_id, kind)</c> — e quem chama sempre quer
    /// "o e-mail desta igreja", nunca "o conector 47".
    /// </remarks>
    Task<TenantConnector?> FindByKindAsync(ConnectorKind kind, CancellationToken cancellationToken);

    void Add(TenantConnector connector);

    void Remove(TenantConnector connector);
}

/// <summary>O que uma tentativa de conexão descobriu.</summary>
public sealed record ConnectorTestResult
{
    public required bool Succeeded { get; init; }

    /// <summary>
    /// O que aconteceu, em português e para quem configurou.
    /// </summary>
    /// <remarks>
    /// "Autenticação recusada — confira a senha de aplicativo" resolve o
    /// problema; <c>535 5.7.8</c> manda a pessoa pesquisar o código na internet.
    /// </remarks>
    public required string Message { get; init; }
}

/// <summary>
/// Tenta falar com o serviço externo, de verdade.
/// </summary>
/// <remarks>
/// <b>Nunca lança por falha do serviço.</b> Credencial errada e servidor fora do
/// ar são o resultado esperado deste método, não exceção: quem chama quer
/// gravar o diagnóstico no conector, e uma exceção transformaria "o teste
/// reprovou" em "o teste quebrou".
/// </remarks>
public interface IConnectorTester
{
    ConnectorKind Kind { get; }

    Task<ConnectorTestResult> TestAsync(
        IReadOnlyDictionary<string, string> settings,
        string secret,
        CancellationToken cancellationToken);
}
