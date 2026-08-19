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

    /// <summary>
    /// Hash do código de retirada de criança.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mesmo raciocínio do OTP, e por isso <b>também com pepper</b>: o código é
    /// curto o bastante para caber inteiro numa tabela pré-computada, e só o
    /// segredo que vive fora do banco torna o hash inútil para quem tem o dump.
    /// </para>
    /// <para>
    /// <b>Pepper próprio, não o do OTP.</b> Os dois têm ciclo de vida
    /// independente — rotacionar o pepper de autenticação invalidaria todos os
    /// códigos de retirada em circulação no meio de um culto, e quem fizesse a
    /// rotação não teria como prever isso. Segredos que giram em ritmos
    /// diferentes não compartilham material.
    /// </para>
    /// </remarks>
    byte[] HashPickupCode(string code);
}

/// <summary>
/// Criptografia de campo, para os dados que nem o DBA pode ler.
/// </summary>
/// <remarks>
/// <para>
/// Existe por causa do ADR-014: alergia, condição de saúde e observações de
/// criança são a classe de maior severidade do sistema, e o critério de
/// aceitação escrito lá é literal — <b>"o DBA não deve conseguir ler esses
/// campos com um <c>SELECT</c>"</b>. Criptografia de disco não satisfaz isso:
/// ela protege o disco roubado, não a consulta autenticada.
/// </para>
/// <para>
/// <b>Na aplicação, não no banco.</b> <c>pgcrypto</c> receberia a chave como
/// argumento de função — e ela apareceria no log de query e no
/// <c>pg_stat_statements</c>, exatamente onde o ADR diz que a chave não pode
/// estar. A chave vive no secret manager e nunca sai do processo.
/// </para>
/// <para>
/// <b>Cifrar o mesmo texto duas vezes produz bytes diferentes</b>, porque cada
/// operação usa nonce novo. A consequência é que não dá para consultar por
/// esses campos — e isso é correto, não limitação: ninguém busca criança por
/// alergia, e um esquema determinístico que permitisse a busca vazaria quais
/// registros têm o mesmo valor.
/// </para>
/// </remarks>
public interface IFieldEncryptor
{
    /// <summary>Cifra. Devolve <c>null</c> para entrada nula — ausência não é segredo.</summary>
    byte[]? Encrypt(string? plaintext);

    /// <summary>
    /// Decifra, ou lança se o texto cifrado foi adulterado.
    /// </summary>
    /// <remarks>
    /// Falhar é o comportamento correto sob adulteração: o modo autenticado
    /// detecta a alteração de um único bit, e devolver texto claro parcial ou
    /// silenciar o erro entregaria dado corrompido como se fosse íntegro — numa
    /// ficha de alergia, com consequência física.
    /// </remarks>
    string? Decrypt(byte[]? ciphertext);
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
