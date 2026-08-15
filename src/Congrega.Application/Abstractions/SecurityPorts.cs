namespace Congrega.Application.Abstractions;

/// <summary>
/// Operações criptográficas usadas pela autenticação.
/// </summary>
/// <remarks>
/// Existe para manter <c>System.Security.Cryptography</c> fora do domínio e da
/// aplicação, e para concentrar em um único ponto as escolhas que não podem ser
/// improvisadas caso a caso. Nunca implemente criptografia própria atrás desta
/// interface — só primitivas consolidadas.
/// </remarks>
public interface ISecretHasher
{
    /// <summary>
    /// Hash do código OTP.
    /// </summary>
    /// <remarks>
    /// <b>HMAC com pepper, não hash simples.</b> Um OTP de 6 dígitos tem 10⁶
    /// combinações: uma rainbow table cobre o espaço inteiro em segundos, e SHA-256
    /// puro não ajudaria em nada se o banco vazasse. O pepper vive no secret manager,
    /// fora do banco, e é o que torna o hash inútil para quem só tem o dump.
    /// </remarks>
    byte[] HashOtp(string code);

    /// <summary>
    /// Hash do refresh token. SHA-256 sem pepper é suficiente aqui: o token tem 256
    /// bits de entropia, então não há espaço de busca a proteger.
    /// </summary>
    byte[] HashToken(string tokenValue);

    /// <summary>
    /// Comparação em tempo constante.
    /// </summary>
    /// <remarks>
    /// <c>==</c> e <c>SequenceEqual</c> retornam mais rápido quanto mais cedo os
    /// bytes divergem, e essa diferença é mensurável pela rede. Toda comparação de
    /// material secreto passa por aqui.
    /// </remarks>
    bool FixedTimeEquals(byte[] left, byte[] right);
}

/// <summary>Geração de códigos OTP.</summary>
public interface IOtpGenerator
{
    /// <summary>
    /// Gera um código numérico de 6 dígitos com CSPRNG.
    /// </summary>
    /// <remarks>
    /// <c>System.Random</c> é previsível a partir de algumas amostras — inaceitável
    /// para material de autenticação. A implementação usa
    /// <c>RandomNumberGenerator</c>.
    /// </remarks>
    string Generate();
}

/// <summary>Dados necessários para emitir um access token.</summary>
public sealed record AccessTokenRequest
{
    public required long UserId { get; init; }
    public required Guid UserPublicId { get; init; }
    public required string Email { get; init; }
    public required bool EmailVerified { get; init; }
    public long? TenantId { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public string? SubscriptionTier { get; init; }
}

public sealed record IssuedAccessToken
{
    public required string Value { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string JwtId { get; init; }
}

public interface ITokenIssuer
{
    /// <summary>Emite o access token assinado em RS256, com as claims documentadas.</summary>
    IssuedAccessToken IssueAccessToken(AccessTokenRequest request);

    /// <summary>
    /// Gera o valor opaco do refresh token: 256 bits de CSPRNG, codificados em
    /// base64url. Opaco, e não JWT — um JWT de refresh seria autocontido e,
    /// portanto, irrevogável sem lista negra.
    /// </summary>
    string GenerateRefreshTokenValue();
}

/// <summary>
/// Tier de assinatura do usuário, para a claim <c>subscription_tier</c>.
/// </summary>
/// <remarks>
/// <b>A claim é conveniência de interface, nunca autorização.</b> A decisão de acesso
/// a conteúdo consulta <c>entitlements</c> no banco, a cada requisição. Um token
/// emitido às 10h continua dizendo "premium" às 10h14 mesmo se a assinatura foi
/// cancelada às 10h05 — usar a claim para liberar download concede 15 minutos de
/// acesso indevido a cada cancelamento.
/// </remarks>
public interface ISubscriptionTierProvider
{
    Task<string?> GetActiveTierAsync(long userId, CancellationToken cancellationToken);
}

/// <summary>Fronteira transacional explícita.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persiste as alterações e drena os eventos de domínio para o Outbox
    /// <b>na mesma transação</b>. É essa atomicidade que elimina a janela entre
    /// "gravou no banco" e "publicou a mensagem".
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Publicação de mensagens via Outbox.</summary>
public interface IOutbox
{
    /// <summary>
    /// Enfileira uma mensagem. Só é efetivada no <see cref="IUnitOfWork.SaveChangesAsync"/>,
    /// junto com a mudança de estado que a originou.
    /// </summary>
    void Enqueue(string messageType, object payload, string? correlationId = null);
}
