using Congrega.Domain.Common;

namespace Congrega.Domain.Calendar;

public enum EventStatus : short
{
    Agendado = 1,
    Cancelado = 2,
}

/// <summary>
/// Natureza do evento. Espelha <c>events.event_type</c>.
/// </summary>
/// <remarks>
/// <b>Dado, não bifurcação de lógica.</b> Nenhuma regra do domínio muda com o
/// tipo — ele existe para a agenda agrupar e a interface diferenciar. Assim que
/// alguma regra passar a depender dele (quem pode agendar culto, por exemplo),
/// isso vira permissão, não um <c>switch</c> aqui dentro.
///
/// <para>
/// <c>Outro</c> é o padrão de quem não classificou, e é o único valor que não
/// afirma nada falso sobre um evento antigo.
/// </para>
/// </remarks>
public enum EventType : short
{
    Culto = 1,
    Reuniao = 2,
    Estudo = 3,
    Ensaio = 4,
    Outro = 5,
}

/// <summary>
/// Um evento da agenda da igreja — culto, reunião de oração, ensaio, batismo.
/// </summary>
/// <remarks>
/// <para>
/// Uma ocorrência concreta, sem recorrência. Ver a nota em
/// <c>db/007_eventos.sql</c>: recorrência de verdade exige regra, exceções,
/// materialização e horário de verão, e nada disso é "barato de construir" —
/// que é a justificativa com que o doc 05 colocou o calendário no MVP.
/// </para>
/// <para>
/// Instantes são <see cref="DateTimeOffset"/> e são <b>normalizados para UTC na
/// entrada</b>. A conversão para <c>America/Sao_Paulo</c> acontece na borda: o
/// domínio não sabe em que fuso a igreja está, e não deve saber.
/// </para>
/// <para>
/// A normalização não é cosmética. O Npgsql recusa gravar um
/// <see cref="DateTimeOffset"/> com offset diferente de zero em
/// <c>timestamptz</c>, e o cliente manda <c>-03:00</c> — foi um 500 na primeira
/// tentativa de agendar culto. Guardar o offset original também faria a resposta
/// da criação (<c>-03:00</c>) divergir da resposta de leitura (<c>+00:00</c>)
/// para o mesmo evento.
/// </para>
/// </remarks>
public sealed class CalendarEvent : AggregateRoot
{
    private CalendarEvent()
    {
        Title = string.Empty;
    }

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long TenantId { get; private set; }

    public string Title { get; private set; }
    public string? Description { get; private set; }
    public string? Location { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public EventStatus Status { get; private set; }
    public EventType Type { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CalendarEvent Schedule(
        long tenantId,
        string title,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now,
        string? description = null,
        string? location = null,
        EventType type = EventType.Outro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        EnsurePeriodoValido(startsAt, endsAt);

        return new CalendarEvent
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Title = NormalizeTitle(title),
            Description = Blank(description),
            Location = Blank(location),
            StartsAt = startsAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            Status = EventStatus.Agendado,
            Type = type,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Edita título, descrição, local e horário de uma vez.</summary>
    /// <remarks>
    /// Um método só, e não um por campo: a tela edita tudo junto, e três
    /// chamadas separadas abririam a chance de salvar metade — um evento com o
    /// título novo e o horário antigo.
    /// </remarks>
    public void Update(
        string title,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset now,
        string? description = null,
        string? location = null,
        EventType? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsurePeriodoValido(startsAt, endsAt);

        Title = NormalizeTitle(title);
        Description = Blank(description);
        Location = Blank(location);
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();

        // Nulo mantém o tipo atual: quem edita só o horário não deve reclassificar
        // o evento como "Outro" por omissão.
        if (type is { } novoTipo)
        {
            Type = novoTipo;
        }

        UpdatedAt = now;
    }

    /// <summary>
    /// Cancela sem apagar.
    /// </summary>
    /// <remarks>
    /// O evento cancelado continua na agenda, marcado. Apagá-lo faria quem já
    /// sabia do culto aparecer na porta da igreja fechada — a ausência não
    /// comunica cancelamento, e o cancelamento é justamente a informação nova.
    /// </remarks>
    public void Cancel(DateTimeOffset now)
    {
        Status = EventStatus.Cancelado;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        Status = EventStatus.Agendado;
        UpdatedAt = now;
    }

    private static void EnsurePeriodoValido(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException(
                "O fim do evento precisa ser depois do começo.", nameof(endsAt));
        }
    }

    private static string NormalizeTitle(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
