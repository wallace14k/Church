using Congrega.Domain.Common;

namespace Congrega.Domain.Giving;

public enum GivingMethod : short
{
    Dinheiro = 1,
    Pix = 2,
    Cartao = 3,
    Transferencia = 4,
    Cheque = 5,
    Outro = 6,
}

/// <summary>
/// Um lançamento de caixa — dinheiro que entrou ou saiu, na data em que isso
/// aconteceu.
/// </summary>
public sealed class GivingEntry : AggregateRoot
{
    private GivingEntry() { }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }
    public long CategoryId { get; private set; }

    /// <summary>
    /// Doador identificado, quando houver.
    /// </summary>
    /// <remarks>
    /// Nulo é o caso <b>comum</b>, não a exceção: oferta de gazofilácio não tem
    /// nome. Exigir membro impediria de lançar justamente a receita mais
    /// frequente da igreja.
    /// </remarks>
    public long? MemberId { get; private set; }

    /// <summary>Centavos, sempre positivo. Ver <see cref="GivingKind"/>.</summary>
    public long AmountCents { get; private set; }

    public DateOnly OccurredOn { get; private set; }
    public GivingMethod Method { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Conta que digitou o lançamento. Prestação de contas precisa da autoria.</summary>
    public long? RecordedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GivingEntry Register(
        long tenantId,
        long categoryId,
        long amountCents,
        DateOnly occurredOn,
        GivingMethod method,
        DateTimeOffset now,
        long? memberId = null,
        string? notes = null,
        long? recordedByUserId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryId);

        if (amountCents <= 0)
        {
            throw new ArgumentException(
                "O valor precisa ser maior que zero. Saída é definida pela categoria, não por valor negativo.",
                nameof(amountCents));
        }

        if (!Enum.IsDefined(method))
        {
            throw new ArgumentException("Forma de pagamento inválida.", nameof(method));
        }

        // Data futura em livro-caixa é erro de digitação — quase sempre o ano.
        // Sem a barreira, um lançamento de 2027 sairia silenciosamente do
        // fechamento do mês e ninguém acharia o dinheiro que "sumiu".
        var hoje = DateOnly.FromDateTime(now.UtcDateTime);
        if (occurredOn > hoje)
        {
            throw new ArgumentException(
                "A data do lançamento não pode ser futura.", nameof(occurredOn));
        }

        return new GivingEntry
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = categoryId,
            MemberId = memberId,
            AmountCents = amountCents,
            OccurredOn = occurredOn,
            Method = method,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RecordedByUserId = recordedByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
