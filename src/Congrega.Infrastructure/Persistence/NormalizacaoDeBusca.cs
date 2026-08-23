using System.Globalization;
using System.Text;

namespace Congrega.Infrastructure.Persistence;

/// <summary>
/// Normalização do termo digitado, para busca sem acento.
/// </summary>
/// <remarks>
/// <para>
/// Extraído de <c>MemberRepository</c> quando a busca de lançamentos passou a
/// precisar do mesmo tratamento. Duas cópias divergiriam na primeira correção —
/// e o sintoma seria sutil: uma tela encontraria "João" digitando "joao" e a
/// outra não, sem nenhum erro em lugar nenhum.
/// </para>
/// <para>
/// <b>Normaliza apenas o lado .NET.</b> O lado do banco usa
/// <c>CongregaDbContext.Unaccent</c>, que mapeia para a função
/// <c>congrega_unaccent</c> — e a expressão precisa ser <b>idêntica</b> à do
/// índice de trigramas, senão o índice existe sem nunca ser usado: custo de
/// escrita em toda inserção, zero benefício na leitura.
/// </para>
/// </remarks>
internal static class NormalizacaoDeBusca
{
    /// <summary>
    /// Remove os diacríticos de um texto — "João" vira "Joao".
    /// </summary>
    /// <remarks>
    /// Decompõe em forma canônica (<c>FormD</c>), descarta as marcas que não
    /// ocupam espaço próprio, e recompõe. É a forma que trata "ç", "ã" e "ê" com
    /// a mesma regra, em vez de uma tabela de substituição que esquece um deles.
    /// </remarks>
    public static string RemoverAcentos(string texto)
    {
        string decomposto = texto.Normalize(NormalizationForm.FormD);

        var construtor = new StringBuilder(decomposto.Length);
        foreach (char caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
            {
                construtor.Append(caractere);
            }
        }

        return construtor.ToString().Normalize(NormalizationForm.FormC);
    }
}
