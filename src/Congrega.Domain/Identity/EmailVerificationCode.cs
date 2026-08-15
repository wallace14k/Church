using Congrega.Domain.Common;

namespace Congrega.Domain.Identity;

/// <summary>Finalidade do código. Um código emitido para login não serve para trocar e-mail.</summary>
public enum OtpPurpose
{
    Login = 1,
    EmailChange = 2,
    Recovery = 3
}

/// <summary>Resultado da tentativa de validação de um código.</summary>
public enum OtpValidationResult
{
    Valid = 0,
    NotFound = 1,
    Expired = 2,
    AlreadyConsumed = 3,
    TooManyAttempts = 4,
    Mismatch = 5
}

/// <summary>
/// Código OTP de uso único enviado por e-mail.
/// </summary>
/// <remarks>
/// <para>
/// O agregado <b>nunca</b> vê o código em texto plano depois de criado: ele guarda
/// apenas o hash e recebe hashes para comparar. Isso torna impossível, por
/// construção, que o valor vaze em log, em DTO ou em exceção.
/// </para>
/// <para>
/// A ordem das verificações dentro de <see cref="Validate"/> é deliberada e vale
/// como regra de revisão — ver o comentário no método.
/// </para>
/// </remarks>
public sealed class EmailVerificationCode : AggregateRoot
{
    public const int DefaultMaxAttempts = 5;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private EmailVerificationCode()
    {
        // Exigido pelo EF Core.
        CodeHash = [];
    }

    public long Id { get; private set; }
    public long UserId { get; private set; }
    public byte[] CodeHash { get; private set; }
    public OtpPurpose Purpose { get; private set; }
    public short AttemptCount { get; private set; }
    public short MaxAttempts { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? RequestIp { get; private set; }

    public static EmailVerificationCode Issue(
        long userId,
        byte[] codeHash,
        OtpPurpose purpose,
        DateTimeOffset now,
        string? requestIp = null,
        TimeSpan? lifetime = null,
        short maxAttempts = DefaultMaxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfZero(codeHash.Length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        return new EmailVerificationCode
        {
            UserId = userId,
            CodeHash = codeHash,
            Purpose = purpose,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime ?? DefaultLifetime),
            RequestIp = requestIp
        };
    }

    /// <summary>
    /// Valida um hash candidato, consumindo uma tentativa.
    /// </summary>
    /// <param name="candidateHash">Hash do código informado pelo usuário.</param>
    /// <param name="hashComparer">
    /// Comparação em tempo constante, injetada pela camada de aplicação. O domínio
    /// não referencia <c>System.Security.Cryptography</c>, mas também não pode
    /// aceitar uma comparação ingênua — daí o delegate em vez de <c>SequenceEqual</c>.
    /// </param>
    public OtpValidationResult Validate(
        byte[] candidateHash,
        Func<byte[], byte[], bool> hashComparer,
        DateTimeOffset now)
    {
        // 1. Estado terminal primeiro: consumido e expirado não gastam tentativa,
        //    porque não há o que proteger — o código já não vale mais.
        if (ConsumedAt is not null)
        {
            return OtpValidationResult.AlreadyConsumed;
        }

        if (now >= ExpiresAt)
        {
            return OtpValidationResult.Expired;
        }

        if (AttemptCount >= MaxAttempts)
        {
            return OtpValidationResult.TooManyAttempts;
        }

        // 2. Incrementa ANTES de comparar. Se a comparação lançasse — ou se um
        //    retorno antecipado fosse acrescentado por engano depois dela — o
        //    atacante teria tentativas de graça. Contar primeiro torna a proteção
        //    independente do que acontece adiante.
        AttemptCount++;

        // 3. Comparação em tempo constante. Comparar com == ou SequenceEqual
        //    retorna mais rápido quanto mais cedo diverge o primeiro byte, e essa
        //    diferença é mensurável pela rede.
        if (!hashComparer(candidateHash, CodeHash))
        {
            return OtpValidationResult.Mismatch;
        }

        // 4. Uso único: consumir aqui e não em outro método impede que um caminho
        //    de código valide sem consumir.
        ConsumedAt = now;
        return OtpValidationResult.Valid;
    }

    /// <summary>Invalida o código sem consumi-lo — usado ao emitir um novo para o mesmo e-mail.</summary>
    public void Invalidate(DateTimeOffset now)
    {
        if (ConsumedAt is null)
        {
            ExpiresAt = now;
        }
    }

    public bool IsActive(DateTimeOffset now) =>
        ConsumedAt is null && now < ExpiresAt && AttemptCount < MaxAttempts;
}
