using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// Verificação HMAC-SHA256 do webhook, com proteção de replay por janela de
/// tempo.
/// </summary>
/// <remarks>
/// <para>
/// Formato do cabeçalho, no padrão que os gateways usam:
/// <c>t=1700000000,v1=&lt;hex do hmac&gt;</c>. O timestamp entra <b>dentro</b> do
/// que é assinado (<c>{t}.{payload}</c>) — se ficasse fora, um atacante poderia
/// trocar o <c>t</c> de um evento capturado e reapresentá-lo para sempre.
/// </para>
/// <para>
/// Três controles, e os três precisam existir:
/// </para>
/// <list type="number">
/// <item>a assinatura confere com o segredo compartilhado (autenticidade);</item>
/// <item>o timestamp está dentro da tolerância (replay);</item>
/// <item>a comparação é em tempo constante (oráculo de temporização).</item>
/// </list>
/// <para>
/// O terceiro parece exagero e não é: comparar com <c>==</c> vaza, pelo tempo de
/// resposta, quantos bytes iniciais da assinatura estavam certos, e isso permite
/// forjar a assinatura byte a byte sem nunca conhecer o segredo.
/// </para>
/// </remarks>
internal sealed class WebhookSignatureVerifier(IOptions<PaymentOptions> options)
    : IWebhookSignatureVerifier
{
    private readonly PaymentOptions _options = options.Value;

    public bool IsValid(string payload, string? signatureHeader, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(_options.WebhookSecret))
        {
            return false;
        }

        if (!TryParse(signatureHeader, out long timestamp, out string? assinaturaRecebida)
            || assinaturaRecebida is null)
        {
            return false;
        }

        // Replay: fora da janela, recusa antes mesmo de calcular o HMAC. Um
        // evento legítimo capturado ontem continua com assinatura válida para
        // sempre — só o timestamp o impede de ser reapresentado.
        var momento = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var distancia = (receivedAt - momento).Duration();
        if (distancia > _options.WebhookTolerance)
        {
            return false;
        }

        // `InvariantCulture` na formatação do timestamp: com uma cultura que usa
        // separador de milhar, o número viraria "1.700.000.000" e o HMAC nunca
        // bateria. É o mesmo tipo de bug que já mordeu este projeto no `::bigint`
        // das policies de RLS.
        string assinado = string.Create(
            CultureInfo.InvariantCulture, $"{timestamp}.{payload}");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
        byte[] esperado = hmac.ComputeHash(Encoding.UTF8.GetBytes(assinado));

        if (!TryFromHex(assinaturaRecebida, out byte[]? recebido))
        {
            return false;
        }

        // Tempo constante. Ver a nota da classe.
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }

    private static bool TryParse(string header, out long timestamp, out string? signature)
    {
        timestamp = 0;
        signature = null;

        foreach (var parte in header.Split(',', StringSplitOptions.TrimEntries))
        {
            int igual = parte.IndexOf('=', StringComparison.Ordinal);
            if (igual <= 0)
            {
                continue;
            }

            string chave = parte[..igual];
            string valor = parte[(igual + 1)..];

            if (chave.Equals("t", StringComparison.Ordinal))
            {
                if (!long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out timestamp))
                {
                    return false;
                }
            }
            else if (chave.Equals("v1", StringComparison.Ordinal))
            {
                signature = valor;
            }
        }

        return timestamp > 0 && !string.IsNullOrEmpty(signature);
    }

    private static bool TryFromHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            // Assinatura malformada é recusa, não exceção que sobe: o
            // remetente é entrada não confiável por definição.
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// Gera um cabeçalho no mesmo formato — usado pelos testes e pelo gateway de
    /// desenvolvimento para exercer o caminho de verificação de verdade.
    /// </summary>
    /// <remarks>
    /// Mora aqui, junto da verificação, para que as duas nunca divirjam de
    /// formato. Um gerador escrito à parte no teste provaria apenas que o teste
    /// concorda consigo mesmo.
    /// </remarks>
    public static string BuildHeader(string payload, string secret, DateTimeOffset moment)
    {
        long timestamp = moment.ToUnixTimeSeconds();
        string assinado = string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{payload}");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        string hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(assinado))).ToLowerInvariant();

        return string.Create(CultureInfo.InvariantCulture, $"t={timestamp},v1={hex}");
    }
}
