using System.ComponentModel.DataAnnotations;
using Congrega.Application.Abstractions;
using Congrega.Domain.Addressing;

namespace Congrega.Api.Endpoints;

/// <summary>
/// Endereço no corpo de uma requisição de membro ou de evento.
/// </summary>
/// <remarks>
/// Um tipo só para os dois, e não um por dono: as regras de tipo de residência
/// e de normalização de CEP são as mesmas, e duas cópias divergiriam na
/// primeira mudança — que é exatamente o motivo de o endereço ter virado
/// entidade.
/// </remarks>
public sealed record AddressPayload
{
    [MaxLength(9)]
    public string? Cep { get; init; }

    [MaxLength(200)] public string? Logradouro { get; init; }
    [MaxLength(100)] public string? Bairro { get; init; }
    [MaxLength(100)] public string? Localidade { get; init; }
    [MaxLength(60)]  public string? Estado { get; init; }

    /// <summary><c>Casa</c> ou <c>Apartamento</c>. Ausente vira <c>Casa</c>.</summary>
    [MaxLength(20)]
    public string? ResidenceType { get; init; }

    [MaxLength(Address.MaxNumeroLength)] public string? Numero { get; init; }

    /// <summary>Só faz sentido em apartamento; em casa é descartado.</summary>
    [MaxLength(Address.MaxAndarLength)] public string? Andar { get; init; }
}

/// <summary>O endereço como ele volta para a interface.</summary>
public sealed record AddressResponse
{
    public required Guid Id { get; init; }

    /// <summary>Formatado, <c>01001-000</c>, ou <c>null</c>.</summary>
    public string? Cep { get; init; }

    public string? Logradouro { get; init; }
    public string? Bairro { get; init; }
    public string? Localidade { get; init; }
    public string? Estado { get; init; }
    public required string ResidenceType { get; init; }
    public string? Numero { get; init; }
    public string? Andar { get; init; }
}

/// <summary>
/// Grava, atualiza ou apaga o endereço de um dono, resolvendo os quatro casos.
/// </summary>
/// <remarks>
/// Compartilhado entre membro e evento porque a lógica de ciclo de vida é
/// idêntica, e é sutil o bastante para não valer duas cópias.
/// </remarks>
public static class AddressPayloadExtensions
{
    /// <summary>
    /// Lê o tipo do corpo. Desconhecido ou ausente vira <c>Casa</c>.
    /// </summary>
    /// <remarks>
    /// Não recusa a requisição: o tipo de residência é detalhe do endereço, e
    /// devolver 400 por um rótulo desconhecido impediria de cadastrar um membro
    /// por causa de um campo acessório.
    /// </remarks>
    public static ResidenceType LerTipoDeResidencia(string? valor) =>
        Enum.TryParse<ResidenceType>(valor, ignoreCase: true, out var tipo)
            ? tipo
            : ResidenceType.Casa;

    /// <summary>
    /// Aplica o payload ao endereço atual do dono e devolve a chave resultante.
    /// </summary>
    /// <remarks>
    /// <para>Quatro caminhos, e cada um existe por um motivo:</para>
    /// <list type="bullet">
    /// <item>
    /// <b>Sem payload</b> (<c>null</c>): não mexe. Quem edita só o telefone não
    /// deve perder o endereço por omissão — o mesmo raciocínio do
    /// <c>clearType</c> do evento.
    /// </item>
    /// <item>
    /// <b>Payload vazio</b>: remove. É como a interface diz "apaguei o
    /// endereço", já que um formulário limpo chega como campos em branco.
    /// </item>
    /// <item>
    /// <b>Payload com dados e endereço existente</b>: atualiza <b>a mesma
    /// linha</b>. Criar outra e repontar deixaria a antiga órfã no banco a cada
    /// edição — lixo que só apareceria meses depois, numa tabela crescendo sem
    /// explicação.
    /// </item>
    /// <item>
    /// <b>Payload com dados e nenhum endereço</b>: cria — e <b>grava na hora</b>.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Por que a gravação acontece aqui, e não junto com o dono.</b> A chave
    /// de um endereço novo só existe depois do <c>INSERT</c>: até lá
    /// <c>Id</c> é zero, e um zero atribuído a <c>member.AddressId</c> seria
    /// recusado pela chave estrangeira com um erro que não diz nada sobre a
    /// causa. Como a entidade de domínio guarda a chave e não uma navegação, o
    /// EF não tem como ligar as duas sozinho — então a ordem é explícita.
    /// </para>
    /// <para>
    /// O custo aceito: se a gravação do <b>dono</b> falhar depois desta, a linha
    /// de endereço fica órfã. Órfã é inofensiva — ninguém a referencia e ela não
    /// aparece em tela nenhuma — e é preço menor que o de expor transação
    /// explícita através do <c>IUnitOfWork</c> só para este caso.
    /// </para>
    /// </remarks>
    public static async Task<long?> AplicarAsync(
        this AddressPayload? payload,
        long? atualId,
        long tenantId,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        var atual = atualId is { } id
            ? await addresses.FindByIdAsync(id, cancellationToken)
            : null;

        if (payload is null)
        {
            return atualId;
        }

        var tipo = LerTipoDeResidencia(payload.ResidenceType);

        if (atual is not null)
        {
            atual.Update(
                payload.Cep, payload.Logradouro, payload.Bairro, payload.Localidade,
                payload.Estado, tipo, payload.Numero, payload.Andar, agora);

            if (!atual.IsEmpty)
            {
                return atual.Id;
            }

            // Ficou vazio: some do banco em vez de virar linha em branco
            // pendurada no membro. Um endereço sem nada é ruído que toda tela
            // teria de aprender a esconder.
            addresses.Remove(atual);
            return null;
        }

        var novo = Address.Register(
            tenantId, payload.Cep, payload.Logradouro, payload.Bairro, payload.Localidade,
            payload.Estado, tipo, payload.Numero, payload.Andar, agora);

        if (novo.IsEmpty)
        {
            return null;
        }

        addresses.Add(novo);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return novo.Id;
    }

    public static AddressResponse ToResponse(this Address endereco) => new()
    {
        Id = endereco.PublicId,
        Cep = endereco.Cep is null ? null : PostalCode.Format(endereco.Cep),
        Logradouro = endereco.Logradouro,
        Bairro = endereco.Bairro,
        Localidade = endereco.Localidade,
        Estado = endereco.Estado,
        ResidenceType = endereco.ResidenceType.ToString(),
        Numero = endereco.Numero,
        Andar = endereco.Andar,
    };
}
