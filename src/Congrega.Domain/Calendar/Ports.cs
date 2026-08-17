namespace Congrega.Domain.Calendar;

/// <summary>
/// Janela da agenda.
/// </summary>
/// <remarks>
/// <b>Os dois instantes são normalizados para UTC na atribuição.</b> Não é
/// cosmético: o Npgsql recusa <see cref="DateTimeOffset"/> com offset diferente
/// de zero em <c>timestamptz</c> — inclusive como <b>parâmetro de consulta</b>,
/// e não só na gravação. Normalizar a entidade não cobria este caminho, e a
/// agenda respondia 500 a qualquer filtro vindo do app, que manda <c>-03:00</c>.
/// </remarks>
public sealed record EventQuery
{
    private readonly DateTimeOffset _from;
    private readonly DateTimeOffset _to;

    /// <summary>Início da janela, inclusivo. Evento que começou antes e ainda não terminou entra.</summary>
    public required DateTimeOffset From
    {
        get => _from;
        init => _from = value.ToUniversalTime();
    }

    /// <summary>Fim da janela, exclusivo.</summary>
    public required DateTimeOffset To
    {
        get => _to;
        init => _to = value.ToUniversalTime();
    }

    /// <summary>
    /// Cancelados entram por padrão: um culto cancelado é o item mais
    /// importante da agenda da semana, não ruído a filtrar.
    /// </summary>
    public bool IncludeCanceled { get; init; } = true;
}

public interface IEventRepository
{
    /// <summary>
    /// Eventos que <b>tocam</b> a janela, ordenados por início.
    /// </summary>
    /// <remarks>
    /// Sobreposição, não contenção: um retiro que começa na sexta e termina no
    /// domingo precisa aparecer na consulta de sábado. Filtrar por
    /// <c>StartsAt</c> dentro da janela esconderia justamente o evento em curso.
    /// </remarks>
    Task<IReadOnlyList<CalendarEvent>> ListAsync(EventQuery query, CancellationToken cancellationToken);

    Task<CalendarEvent?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /// <summary>
    /// Próximos eventos a partir de agora — alimenta o painel de início.
    /// </summary>
    /// <remarks>
    /// A implementação normaliza <paramref name="from"/> para UTC; ver a nota em
    /// <see cref="EventQuery"/> sobre por que o offset importa aqui.
    /// </remarks>
    Task<IReadOnlyList<CalendarEvent>> ListUpcomingAsync(
        DateTimeOffset from,
        int limit,
        CancellationToken cancellationToken);

    void Add(CalendarEvent calendarEvent);

    void Remove(CalendarEvent calendarEvent);
}
