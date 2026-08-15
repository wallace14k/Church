using System.Globalization;
using System.Security.Cryptography;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Congrega.Infrastructure.Security;

/// <summary>Nomes das claims. Constantes para que emissor e policies não divirjam por digitação.</summary>
public static class CongregaClaims
{
    public const string Subject = "sub";
    public const string TenantId = "tenant_id";
    public const string Roles = "roles";
    public const string Permissions = "perms";
    public const string SubscriptionTier = "subscription_tier";
    public const string EmailVerified = "email_verified";
    public const string UserPublicId = "upid";
    public const string TokenId = "jti";
}

/// <inheritdoc />
public sealed class JwtTokenIssuer : ITokenIssuer, IDisposable
{
    private const int RefreshTokenBytes = 32;   // 256 bits

    private readonly AuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly RSA _rsa;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenIssuer(IOptions<AuthenticationOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        _rsa = RSA.Create();
        _rsa.ImportFromPem(_options.SigningKeyPem);

        var key = new RsaSecurityKey(_rsa)
        {
            // KeyId vai no header como "kid". É o que permite rotacionar a chave sem
            // derrubar sessões: durante a transição, o verificador escolhe a chave
            // pública certa pelo kid em vez de tentar uma só.
            KeyId = ComputeKeyId(_rsa)
        };

        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    public IssuedAccessToken IssueAccessToken(AccessTokenRequest request)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(_options.AccessTokenLifetime);
        string jwtId = Guid.NewGuid().ToString("N");

        var claims = new Dictionary<string, object>
        {
            [CongregaClaims.Subject] = request.UserId.ToString(CultureInfo.InvariantCulture),
            [CongregaClaims.UserPublicId] = request.UserPublicId.ToString(),
            [CongregaClaims.EmailVerified] = request.EmailVerified,
            [CongregaClaims.TokenId] = jwtId,

            // Arrays mesmo quando vazios: um consumidor que espera lista e recebe
            // ausência precisa tratar dois casos. Manter o formato estável elimina
            // uma classe inteira de bug no cliente.
            [CongregaClaims.Roles] = request.Roles.ToArray(),
            [CongregaClaims.Permissions] = request.Permissions.ToArray()
        };

        // tenant_id só existe quando há igreja selecionada. Emitir "tenant_id": null
        // convidaria o consumidor a tratar null como valor válido; a ausência é
        // inequívoca — este usuário não está atuando em nenhuma igreja.
        if (request.TenantId is { } tenantId)
        {
            claims[CongregaClaims.TenantId] = tenantId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(request.SubscriptionTier))
        {
            claims[CongregaClaims.SubscriptionTier] = request.SubscriptionTier;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = _signingCredentials
        };

        return new IssuedAccessToken
        {
            Value = _handler.CreateToken(descriptor),
            ExpiresAt = expiresAt,
            JwtId = jwtId
        };
    }

    /// <summary>
    /// Valor opaco de 256 bits em base64url.
    /// </summary>
    /// <remarks>
    /// base64url e não base64 padrão: o valor viaja em cookie e, eventualmente, em
    /// URL de deep link. <c>+</c>, <c>/</c> e <c>=</c> exigiriam escape e são fonte
    /// recorrente de bug de "token inválido" que na verdade é token mal transportado.
    /// </remarks>
    public string GenerateRefreshTokenValue() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    /// <summary>
    /// Identificador determinístico da chave, derivado da própria chave pública.
    /// </summary>
    /// <remarks>
    /// Determinístico de propósito: todas as réplicas calculam o mesmo <c>kid</c> para
    /// a mesma chave, sem precisar coordenar nada. Um GUID aleatório por instância
    /// faria cada pod anunciar um kid diferente para a mesma chave.
    /// </remarks>
    private static string ComputeKeyId(RSA rsa)
    {
        byte[] publicKey = rsa.ExportSubjectPublicKeyInfo();
        byte[] hash = SHA256.HashData(publicKey);
        return Base64UrlEncoder.Encode(hash[..8]);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
