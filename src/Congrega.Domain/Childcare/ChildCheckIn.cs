using Congrega.Domain.Common;

namespace Congrega.Domain.Childcare;

/// <summary>Espelha <c>child_checkins.status</c>.</summary>
public enum CheckInStatus : short
{
    /// <summary>Criança no berçário, aguardando retirada.</summary>
    Present = 1,

    /// <summary>Retirada por responsável autorizado, com código válido.</summary>
    PickedUp = 2,

    /// <summary>Encerrado sem retirada registrada — o evento acabou.</summary>
    Expired = 3,
}

/// <summary>Motivo pelo qual uma tentativa de retirada foi recusada.</summary>
public enum PickupRefusal
{
    /// <summary>Código não confere com o desta criança.</summary>
    WrongCode,

    /// <summary>Código correto, prazo vencido.</summary>
    CodeExpired,

    /// <summary>Quem apresentou não está na lista de autorizados a retirar.</summary>
    NotAuthorized,

    /// <summary>Este check-in já foi encerrado.</summary>
    AlreadyClosed,
}

/// <summary>
/// Tentativa de retirada recusada.
/// </summary>
/// <remarks>
/// O ADR-014 chama isto de "o evento que mais importa detectar em tempo real
/// neste sistema inteiro". Por isso é evento de domínio e não um simples
/// <c>return false</c>: ele precisa chegar ao alerta, e o caminho do Outbox é o
/// que garante que chega mesmo se o processo cair logo depois.
/// </remarks>
public sealed record ChildPickupRefused(
    long CheckInId,
    long ChildId,
    long TenantId,
    PickupRefusal Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ChildPickedUp(
    long CheckInId,
    long ChildId,
    long PickedUpByMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// Uma criança no berçário durante um evento.
/// </summary>
/// <remarks>
/// <para>
/// <b>O código de retirada só existe em texto uma vez</b>, no instante do
/// check-in, para ser impresso na etiqueta do responsável. O agregado guarda
/// apenas o HMAC — recuperá-lo depois é impossível por construção, e é isso que
/// torna o dump do banco inútil para quem quisesse a lista de códigos.
/// </para>
/// <para>
/// <b>Três condições, verificadas na ordem certa.</b> Autorização primeiro,
/// depois validade, depois o código. A ordem importa: conferir o código antes
/// da autorização transformaria a resposta num oráculo — quem tivesse o código
/// certo mas não a autorização receberia um erro diferente de quem errou o
/// código, e a diferença ensina.
/// </para>
/// </remarks>
public sealed class ChildCheckIn : AggregateRoot
{
    private ChildCheckIn()
    {
        PickupCodeHash = [];
        IdempotencyKey = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public long ChildId { get; private set; }
    public long EventId { get; private set; }

    public DateTimeOffset CheckedInAt { get; private set; }
    public long CheckedInBy { get; private set; }

    public byte[] PickupCodeHash { get; private set; }
    public DateTimeOffset PickupCodeExpiresAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }
    public long? PickedUpByMemberId { get; private set; }

    public CheckInStatus Status { get; private set; }

    /// <summary>
    /// Chave estável da operação, vinda do dispositivo.
    /// </summary>
    /// <remarks>
    /// Gerada no tablet no momento do toque, não no servidor: é isso que faz a
    /// fila offline reapresentar a MESMA operação depois que o Wi-Fi volta, em
    /// vez de criar uma segunda entrada para a mesma criança.
    /// </remarks>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Registra a entrada. <paramref name="pickupCodeHash"/> já vem do
    /// <c>ISecretHasher</c> — o texto do código nunca chega aqui.
    /// </summary>
    public static ChildCheckIn Open(
        long tenantId,
        long childId,
        long eventId,
        long checkedInBy,
        byte[] pickupCodeHash,
        DateTimeOffset codeExpiresAt,
        string idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfZero(pickupCodeHash.Length);

        if (codeExpiresAt <= now)
        {
            // Um código que nasce vencido impediria a retirada da criança que
            // acabou de entrar — e o balcão descobriria isso só na saída, com o
            // responsável esperando.
            throw new ArgumentException(
                "O código de retirada não pode nascer vencido.", nameof(codeExpiresAt));
        }

        return new ChildCheckIn
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            ChildId = childId,
            EventId = eventId,
            CheckedInAt = now,
            CheckedInBy = checkedInBy,
            PickupCodeHash = pickupCodeHash,
            PickupCodeExpiresAt = codeExpiresAt,
            Status = CheckInStatus.Present,
            IdempotencyKey = idempotencyKey.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Tenta a retirada. Devolve o motivo da recusa, ou <c>null</c> em sucesso.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A comparação do código é delegada a <paramref name="codesMatch"/> —
    /// tempo constante, vindo do <c>ISecretHasher</c>. O domínio não compara
    /// bytes de segredo por conta própria: <c>SequenceEqual</c> retorna mais
    /// cedo quanto antes divergirem, e a diferença é mensurável.
    /// </para>
    /// <para>
    /// <b>Toda recusa emite evento.</b> Inclusive "código errado", que é o caso
    /// que o ADR-014 quer ver em tempo real. Recusar em silêncio deixaria uma
    /// tentativa de levar criança errada indistinguível de um erro de digitação.
    /// </para>
    /// </remarks>
    public PickupRefusal? TryPickUp(
        byte[] presentedCodeHash,
        long byMemberId,
        bool isAuthorizedGuardian,
        Func<byte[], byte[], bool> codesMatch,
        DateTimeOffset now)
    {
        if (Status != CheckInStatus.Present)
        {
            return Recusar(PickupRefusal.AlreadyClosed, now);
        }

        // Autorização antes do código: ver a nota de ordem na classe.
        if (!isAuthorizedGuardian)
        {
            return Recusar(PickupRefusal.NotAuthorized, now);
        }

        if (now > PickupCodeExpiresAt)
        {
            return Recusar(PickupRefusal.CodeExpired, now);
        }

        if (!codesMatch(PickupCodeHash, presentedCodeHash))
        {
            return Recusar(PickupRefusal.WrongCode, now);
        }

        Status = CheckInStatus.PickedUp;
        PickedUpAt = now;
        PickedUpByMemberId = byMemberId;
        UpdatedAt = now;

        Raise(new ChildPickedUp(Id, ChildId, byMemberId, now));
        return null;
    }

    /// <summary>
    /// Encerra sem retirada — o evento acabou e a criança não foi buscada pelo
    /// fluxo normal.
    /// </summary>
    /// <remarks>
    /// Existe para que "presente" signifique presente de verdade. Sem isso, um
    /// check-in de três meses atrás continuaria contando na lista do berçário, e
    /// o índice parcial de presença encheria de linhas mortas.
    /// </remarks>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != CheckInStatus.Present)
        {
            return false;
        }

        Status = CheckInStatus.Expired;
        UpdatedAt = now;
        return true;
    }

    private PickupRefusal Recusar(PickupRefusal motivo, DateTimeOffset now)
    {
        Raise(new ChildPickupRefused(Id, ChildId, TenantId, motivo, now));
        return motivo;
    }
}
