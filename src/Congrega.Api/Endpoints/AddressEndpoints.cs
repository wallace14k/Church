using Congrega.Api.Authorization;
using Congrega.Domain.Addressing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Congrega.Api.Endpoints;

/// <summary>
/// O endereço postal de um CEP.
/// </summary>
/// <remarks>
/// Só os cinco campos que o requisito manda persistir. O restante do payload da
/// ViaCEP — <c>unidade</c>, <c>uf</c>, <c>regiao</c>, <c>ibge</c>, <c>gia</c>,
/// <c>ddd</c>, <c>siafi</c>, <c>complemento</c> — nem é desserializado.
/// </remarks>
public sealed record PostalCodeResponse
{
    /// <summary>Formatado, <c>01001-000</c> — é o que a tela mostra.</summary>
    public required string Cep { get; init; }

    public required string Logradouro { get; init; }
    public required string Bairro { get; init; }
    public required string Localidade { get; init; }
    public required string Estado { get; init; }

    /// <summary>
    /// De onde veio: <c>cache</c> ou <c>viacep</c>.
    /// </summary>
    /// <remarks>
    /// Não é enfeite de diagnóstico — é o que permite verificar, de fora, que a
    /// busca no banco realmente acontece antes da chamada externa. Sem isso, um
    /// cache quebrado seria indistinguível de um cache funcionando, porque as
    /// duas respostas têm exatamente o mesmo conteúdo.
    /// </remarks>
    public required string Source { get; init; }
}

public static class AddressEndpoints
{
    public static void MapAddressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/postal-codes").WithTags("Endereço");

        // Qualquer membro autenticado consulta: quem cadastra um membro ou
        // agenda um evento precisa disto, e são permissões diferentes.
        //
        // **Sem `TenantMember`**, ao contrário do resto: o CEP é dado postal
        // público e global, não informação da igreja. Exigir tenant impediria a
        // consulta de quem ainda está escolhendo em qual igreja entrar.
        group.MapGet("/{cep}", FindAsync)
            .RequireAuthorization()
            .WithSummary("Busca um CEP no banco e, se não achar, na ViaCEP");
    }

    /// <summary>
    /// Resolve um CEP: cache local primeiro, ViaCEP depois.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A ordem é o requisito.</b> O cache evita repetir a chamada externa
    /// para um CEP que qualquer igreja já consultou — e responde em
    /// microssegundos, o que importa numa consulta disparada enquanto a pessoa
    /// digita.
    /// </para>
    /// <para>
    /// <b>404 significa "preencha à mão", e cobre dois casos diferentes de
    /// propósito:</b> o CEP não existe, ou a ViaCEP não respondeu. Os dois levam
    /// a interface ao mesmo lugar — o formulário manual, que o requisito pede —
    /// e distinguir aqui obrigaria a tela a tratar duas falhas com o mesmo
    /// desfecho. A diferença fica no log do adaptador, onde ela é operacional.
    /// </para>
    /// </remarks>
    private static async Task<IResult> FindAsync(
        string cep,
        IPostalCodeRepository cache,
        IPostalCodeLookup lookup,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (PostalCode.Normalize(cep) is not { } normalizado)
        {
            return TypedResults.Problem(
                title: "CEP inválido",
                detail: "Informe os 8 dígitos do CEP.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await cache.FindAsync(normalizado, cancellationToken) is { } local)
        {
            return TypedResults.Ok(ToResponse(local, "cache"));
        }

        if (await lookup.FindAsync(normalizado, cancellationToken) is not { } externo)
        {
            return NaoEncontrado();
        }

        var novo = PostalCode.FromLookup(
            externo.Cep,
            externo.Logradouro,
            externo.Bairro,
            externo.Localidade,
            externo.Estado,
            timeProvider.GetUtcNow());

        // Gravar no cache é efeito colateral da consulta, não pré-condição da
        // resposta: se a gravação falhar, quem perguntou ainda merece o
        // endereço. `SaveAsync` já engole a corrida de duas gravações iguais.
        await cache.SaveAsync(novo, cancellationToken);

        return TypedResults.Ok(ToResponse(novo, "viacep"));
    }

    private static ProblemHttpResult NaoEncontrado() =>
        TypedResults.Problem(
            title: "CEP não encontrado",
            detail: "Não localizamos este CEP. Preencha o endereço manualmente.",
            statusCode: StatusCodes.Status404NotFound);

    private static PostalCodeResponse ToResponse(PostalCode postal, string origem) => new()
    {
        Cep = PostalCode.Format(postal.Cep),
        Logradouro = postal.Logradouro,
        Bairro = postal.Bairro,
        Localidade = postal.Localidade,
        Estado = postal.Estado,
        Source = origem,
    };
}
