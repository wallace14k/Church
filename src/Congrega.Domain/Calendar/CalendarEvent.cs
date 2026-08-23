using Congrega.Domain.Common;

namespace Congrega.Domain.Calendar;

public enum EventStatus : short
{
    Agendado = 1,
    Cancelado = 2,
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
    /// <summary>
    /// Tipo escolhido pela igreja, ou <c>null</c> para evento não classificado.
    /// </summary>
    /// <remarks>
    /// <b>Dado, não bifurcação de lógica.</b> Nenhuma regra do domínio muda com
    /// o tipo — ele existe para a agenda agrupar e a interface diferenciar.
    /// Assim que alguma regra passar a depender dele (quem pode agendar culto,
    /// por exemplo), isso vira permissão, não um <c>switch</c> aqui dentro.
    ///
    /// <para>
    /// Guarda a chave, e não a entidade: carregar <see cref="EventType"/> junto
    /// obrigaria toda leitura da agenda a materializar o tipo inteiro, e o único
    /// campo que a listagem usa dele é o nome. A junção fica no repositório, que
    /// é quem sabe o que a consulta vai mostrar.
    /// </para>
    /// </remarks>
    public long? TypeId { get; private set; }

    /// <summary>
    /// Endereço do evento, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <b>Convive com <see cref="Location"/>, não o substitui.</b> São coisas
    /// diferentes: <c>Location</c> é o nome do lugar como a igreja o chama —
    /// "Templo", "Salão de baixo", "Chácara do irmão João" — e o endereço é onde
    /// fica. Um culto no templo não precisa de CEP; um retiro fora precisa dos
    /// dois, porque "Chácara do irmão João" não cabe num GPS.
    /// </remarks>
    public long? AddressId { get; private set; }

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
        long? typeId = null,
        long? addressId = null)
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
            TypeId = typeId,
            AddressId = addressId,
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
        long? typeId = null,
        bool clearType = false,
        long? addressId = null,
        bool clearAddress = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsurePeriodoValido(startsAt, endsAt);

        Title = NormalizeTitle(title);
        Description = Blank(description);
        Location = Blank(location);
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        // Três estados, não dois. `typeId` nulo com `clearType` falso significa
        // "não mexa no tipo" — quem edita só o horário não deve desclassificar o
        // evento por omissão. `clearType` é o pedido explícito de remover a
        // classificação, que sem a bandeira seria indistinguível do primeiro caso
        // e ficaria impossível de fazer pela API.
        if (clearType)
        {
            TypeId = null;
        }
        else if (typeId is { } novoTipo)
        {
            TypeId = novoTipo;
        }

        // Mesmos três estados do tipo, e pelo mesmo motivo: nulo sem a bandeira
        // é "não mexa", e remover o endereço precisa de um pedido explícito.
        if (clearAddress)
        {
            AddressId = null;
        }
        else if (addressId is { } novoEndereco)
        {
            AddressId = novoEndereco;
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
