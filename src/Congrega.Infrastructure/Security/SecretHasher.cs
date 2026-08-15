using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Congrega.Infrastructure.Security;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Required]
    public required string Issuer { get; init; }

    [Required]
    public required string Audience { get; init; }

    /// <summary>
    /// Chave privada RSA em PEM, vinda do secret manager.
    /// </summary>
    /// <remarks>
    /// RS256 e não HS256: com chave assimétrica, apenas a API emite tokens, e
    /// qualquer serviço futuro pode verificá-los com a pública sem ganhar o poder de
    /// forjar. Com HS256, todo verificador vira emissor em potencial.
    /// </remarks>
    [Required]
    public required string SigningKeyPem { get; init; }

    /// <summary>
    /// Pepper do HMAC do OTP. <b>Nunca</b> versionado, nunca no banco.
    /// </summary>
    /// <remarks>
    /// É o que separa "hash inútil para quem tem o dump" de "10⁶ combinações
    /// quebradas em segundos". Se o banco vazar mas o pepper não, os códigos
    /// continuam protegidos.
    /// </remarks>
    [Required]
    [MinLength(32)]
    public required string OtpPepper { get; init; }

    [Range(typeof(TimeSpan), "00:05:00", "01:00:00")]
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    [Range(typeof(TimeSpan), "1.00:00:00", "90.00:00:00")]
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
}

/// <inheritdoc />
public sealed class SecretHasher : ISecretHasher, IDisposable
{
    private readonly HMACSHA256 _otpHmac;

    public SecretHasher(IOptions<AuthenticationOptions> options)
    {
        // A instância de HMAC é criada uma vez e reutilizada. HMACSHA256 não é
        // thread-safe para uso concorrente do estado interno, mas ComputeHash sobre
        // um array é atômico o suficiente aqui porque cada chamada reinicializa o
        // estado — ainda assim, o serviço é registrado como singleton e o lock
        // abaixo elimina qualquer dúvida sob carga.
        _otpHmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.OtpPepper));
    }

    public byte[] HashOtp(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var bytes = Encoding.UTF8.GetBytes(code);

        lock (_otpHmac)
        {
            return _otpHmac.ComputeHash(bytes);
        }
    }

    /// <summary>
    /// SHA-256 sem pepper.
    /// </summary>
    /// <remarks>
    /// Diferente do OTP, aqui não há espaço de busca a proteger: o token tem 256 bits
    /// de entropia, e nenhuma tabela pré-computada cobre isso. Acrescentar pepper
    /// seria cerimônia sem ganho.
    /// </remarks>
    public byte[] HashToken(string tokenValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenValue);
        return SHA256.HashData(Encoding.UTF8.GetBytes(tokenValue));
    }

    public bool FixedTimeEquals(byte[] left, byte[] right) =>
        CryptographicOperations.FixedTimeEquals(left, right);

    public void Dispose() => _otpHmac.Dispose();
}

/// <inheritdoc />
public sealed class OtpGenerator : IOtpGenerator
{
    private const int Digits = 6;
    private const int UpperBoundExclusive = 1_000_000;

    /// <summary>
    /// Gera um código de 6 dígitos, incluindo os que começam com zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RandomNumberGenerator.GetInt32</c> e não <c>Random</c>: o gerador padrão do
    /// .NET é previsível a partir de algumas amostras, o que é inaceitável para
    /// material de autenticação.
    /// </para>
    /// <para>
    /// O <c>GetInt32</c> descarta valores fora da faixa em vez de aplicar módulo,
    /// então a distribuição é uniforme. Módulo sobre bytes aleatórios enviesaria os
    /// primeiros valores da faixa — sutil, e o bastante para reduzir a entropia real.
    /// </para>
    /// <para>
    /// O <c>PadLeft</c> importa: sem ele, "000123" viraria "123" e o espaço cairia de
    /// 10⁶ para menos, além de quebrar a validação de tamanho no cliente.
    /// </para>
    /// </remarks>
    public string Generate() =>
        RandomNumberGenerator.GetInt32(UpperBoundExclusive)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(Digits, '0');
}
