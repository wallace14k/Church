namespace Congrega.Domain.Addressing;

/// <summary>
/// O que um serviço externo de CEP devolve.
/// </summary>
/// <remarks>
/// Tipo próprio, e não o JSON da ViaCEP: o domínio não deve conhecer o formato
/// de nenhum fornecedor. Trocar a ViaCEP por outro serviço mexe só no adaptador.
/// </remarks>
public sealed record PostalCodeLookupResult
{
    public required string Cep { get; init; }
    public string? Logradouro { get; init; }
    public string? Bairro { get; init; }
    public string? Localidade { get; init; }
    public string? Estado { get; init; }
}

/// <summary>
/// Consulta de CEP num serviço externo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Devolve <c>null</c> tanto para "não existe" quanto para "não deu para
/// perguntar".</b> Parece perda de informação e é deliberado: os dois casos
/// levam a interface ao mesmo lugar — o preenchimento manual, que o requisito
/// pede. Distinguir aqui obrigaria cada chamador a tratar duas falhas que
/// terminam na mesma tela.
/// </para>
/// <para>
/// A distinção que importa fica no <b>log</b>: um CEP inexistente é rotina, e o
/// serviço fora do ar é operação. O adaptador registra os dois de formas
/// diferentes.
/// </para>
/// </remarks>
public interface IPostalCodeLookup
{
    Task<PostalCodeLookupResult?> FindAsync(string cepNormalizado, CancellationToken cancellationToken);
}

/// <summary>Cache local de CEP. Global, sem tenant.</summary>
public interface IPostalCodeRepository
{
    Task<PostalCode?> FindAsync(string cepNormalizado, CancellationToken cancellationToken);

    /// <summary>
    /// Grava o CEP consultado, ignorando corrida com outra requisição.
    /// </summary>
    /// <remarks>
    /// Duas pessoas digitando o mesmo CEP ao mesmo tempo consultariam a ViaCEP
    /// em paralelo e tentariam gravar as duas. A segunda gravação é descartada
    /// pela chave primária, não por um <c>if (!existe)</c> — que teria janela
    /// entre a consulta e a escrita.
    /// </remarks>
    Task SaveAsync(PostalCode postalCode, CancellationToken cancellationToken);
}

public interface IAddressRepository
{
    Task<Address?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    Task<Address?> FindByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// Endereços de um conjunto de donos, para a listagem resolver todos de uma
    /// vez em vez de uma consulta por linha.
    /// </summary>
    Task<IReadOnlyDictionary<long, Address>> ListByIdsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken);

    void Add(Address address);

    void Remove(Address address);
}
