using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Congrega.Domain.Addressing;
using Microsoft.Extensions.Logging;

namespace Congrega.Infrastructure.Addressing;

/// <summary>
/// Consulta de CEP na ViaCEP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nenhuma falha daqui escapa.</b> Timeout, DNS, 500, JSON estranho — tudo
/// vira <c>null</c>, que a interface lê como "preencha à mão". A alternativa
/// seria a indisponibilidade de um serviço de terceiro impedir a secretaria de
/// cadastrar um membro, e cadastrar membro é a função da tela; o CEP é uma
/// conveniência.
/// </para>
/// <para>
/// <b>O <c>CancellationToken</c> do request continua propagado</b>, e é a única
/// exceção que sai daqui: se quem pediu desistiu, insistir é trabalho jogado
/// fora. É por isso que o <c>catch</c> de <see cref="OperationCanceledException"/>
/// rechecha o token antes de engolir — sem essa distinção, um cancelamento do
/// cliente ficaria indistinguível de um timeout nosso.
/// </para>
/// </remarks>
public sealed class ViaCepPostalCodeLookup(
    HttpClient http,
    ILogger<ViaCepPostalCodeLookup> logger) : IPostalCodeLookup
{
    public async Task<PostalCodeLookupResult?> FindAsync(
        string cepNormalizado,
        CancellationToken cancellationToken)
    {
        try
        {
            var resposta = await http.GetAsync($"ws/{cepNormalizado}/json/", cancellationToken);

            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "ViaCEP respondeu {StatusCode} para o CEP {Cep}.",
                    (int)resposta.StatusCode,
                    cepNormalizado);
                return null;
            }

            var conteudo = await resposta.Content.ReadFromJsonAsync<ViaCepResponse>(cancellationToken);

            // CEP inexistente vem como 200 com `{"erro": "true"}` — não como
            // 404. Tratar só o status deixaria passar um endereço todo vazio
            // como se fosse resposta válida.
            if (conteudo is null || conteudo.Erro is not null || string.IsNullOrWhiteSpace(conteudo.Cep))
            {
                logger.LogDebug("ViaCEP não conhece o CEP {Cep}.", cepNormalizado);
                return null;
            }

            return new PostalCodeLookupResult
            {
                Cep = cepNormalizado,
                Logradouro = conteudo.Logradouro,
                Bairro = conteudo.Bairro,
                Localidade = conteudo.Localidade,
                // `estado` (nome por extenso), e não `uf`: é o campo que o
                // requisito manda persistir.
                Estado = conteudo.Estado,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Quem pediu desistiu. Não é falha da ViaCEP e não deve virar
            // "preencha à mão" numa tela que ninguém está mais olhando.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // `TaskCanceledException` sem o token cancelado é o timeout do
            // HttpClient. `LogWarning` e não `LogError`: é indisponibilidade de
            // terceiro com caminho alternativo pronto, não incidente nosso.
            logger.LogWarning(ex, "Falha ao consultar a ViaCEP para o CEP {Cep}.", cepNormalizado);
            return null;
        }
    }

    /// <summary>
    /// O recorte do payload da ViaCEP que interessa.
    /// </summary>
    /// <remarks>
    /// Os campos descartados — <c>unidade</c>, <c>uf</c>, <c>regiao</c>,
    /// <c>ibge</c>, <c>gia</c>, <c>ddd</c>, <c>siafi</c>, <c>complemento</c> —
    /// não estão aqui de propósito: o requisito lista os cinco a persistir, e
    /// desserializar o que não se guarda convida alguém a guardar depois.
    /// </remarks>
    private sealed record ViaCepResponse
    {
        [JsonPropertyName("cep")] public string? Cep { get; init; }
        [JsonPropertyName("logradouro")] public string? Logradouro { get; init; }
        [JsonPropertyName("bairro")] public string? Bairro { get; init; }
        [JsonPropertyName("localidade")] public string? Localidade { get; init; }
        [JsonPropertyName("estado")] public string? Estado { get; init; }

        /// <summary>Presente só quando o CEP não existe. A ViaCEP manda a string "true".</summary>
        [JsonPropertyName("erro")] public string? Erro { get; init; }
    }
}
