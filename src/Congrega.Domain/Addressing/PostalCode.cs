using System.Text.RegularExpressions;

namespace Congrega.Domain.Addressing;

/// <summary>
/// Um CEP e o endereço postal que ele designa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Global, sem tenant.</b> 01001-000 é a Praça da Sé para toda igreja do
/// país. Segue o mesmo padrão de <c>roles</c> e <c>permissions</c>: tabela de
/// referência que não pertence a ninguém.
/// </para>
/// <para>
/// <b>Só entra aqui resposta da ViaCEP.</b> Endereço digitado à mão — quando
/// nem o cache nem a ViaCEP conhecem o CEP — fica na linha de
/// <see cref="Address"/>. Promover o palpite de uma igreja a dado autoritativo
/// o serviria como verdade para todas as outras.
/// </para>
/// </remarks>
public sealed partial class PostalCode
{
    private PostalCode()
    {
        Cep = string.Empty;
        Logradouro = string.Empty;
        Bairro = string.Empty;
        Localidade = string.Empty;
        Estado = string.Empty;
    }

    /// <summary>Oito dígitos, sem hífen.</summary>
    public string Cep { get; private set; }

    public string Logradouro { get; private set; }
    public string Bairro { get; private set; }
    public string Localidade { get; private set; }
    public string Estado { get; private set; }

    /// <summary>Quando a ViaCEP respondeu isto.</summary>
    public DateTimeOffset FetchedAt { get; private set; }

    public static PostalCode FromLookup(
        string cep,
        string? logradouro,
        string? bairro,
        string? localidade,
        string? estado,
        DateTimeOffset now)
    {
        var normalizado = Normalize(cep)
            ?? throw new ArgumentException("CEP precisa ter 8 dígitos.", nameof(cep));

        return new PostalCode
        {
            Cep = normalizado,
            // A ViaCEP devolve string vazia — não nulo — para CEP de cidade
            // inteira, que não tem logradouro nem bairro próprios. Guardar ""
            // é fiel: o campo foi consultado e a resposta é "não há".
            Logradouro = (logradouro ?? string.Empty).Trim(),
            Bairro = (bairro ?? string.Empty).Trim(),
            Localidade = (localidade ?? string.Empty).Trim(),
            Estado = (estado ?? string.Empty).Trim(),
            FetchedAt = now,
        };
    }

    /// <summary>
    /// Reduz um CEP a oito dígitos, ou devolve <c>null</c> se não for um.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É o que faz <c>"01001-000"</c>, <c>"01001000"</c> e <c>" 01001 000 "</c>
    /// encontrarem a mesma linha. Sem normalizar na escrita, o mesmo lugar
    /// entraria no cache duas vezes e a busca acertaria só metade das vezes.
    /// </para>
    /// <para>
    /// Devolve <c>null</c> em vez de lançar: consultar um CEP incompleto é o
    /// estado normal de quem ainda está digitando, não um erro a reportar.
    /// </para>
    /// </remarks>
    public static string? Normalize(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var digitos = ApenasDigitos().Replace(valor, string.Empty);

        return digitos.Length == 8 ? digitos : null;
    }

    /// <summary>Formata para exibição — <c>01001-000</c>.</summary>
    public static string Format(string cepNormalizado) =>
        cepNormalizado.Length == 8
            ? $"{cepNormalizado[..5]}-{cepNormalizado[5..]}"
            : cepNormalizado;

    [GeneratedRegex(@"[^0-9]")]
    private static partial Regex ApenasDigitos();
}
