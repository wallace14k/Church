using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Addressing;
using Congrega.Domain.Calendar;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

/// <summary>
/// O tipo do evento, do jeito que a linha da agenda precisa dele.
/// </summary>
/// <remarks>
/// Recorte deliberado de <c>EventTypeResponse</c>: sem <c>isActive</c> e sem
/// <c>eventCount</c>, que dizem respeito a administrar o vocabulário, não a
/// desenhar um evento. Mandar o objeto inteiro aqui faria a listagem carregar
/// campos que a tela ignora — e convidaria alguém a decidir alguma coisa pelo
/// <c>isActive</c> de um evento histórico, que continua classificado mesmo
/// depois de o tipo sair do formulário.
/// </remarks>
public sealed record EventTypeRef
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }

    /// <summary>
    /// Cor do tipo, <c>#RRGGBB</c> ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Vem junto do evento em vez de a agenda cruzar com a lista de tipos: a
    /// listagem desenha a faixa de cada linha, e resolver a cor no cliente
    /// exigiria uma segunda requisição só para pintar uma barra.
    /// </remarks>
    public string? ColorHex { get; init; }
}

public sealed record EventResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public required DateTimeOffset StartsAt { get; init; }
    public required DateTimeOffset EndsAt { get; init; }
    /// <summary>`Agendado` ou `Cancelado`.</summary>
    public required string Status { get; init; }

    /// <summary>
    /// Tipo do evento, ou <c>null</c> quando não classificado.
    /// </summary>
    /// <remarks>
    /// Objeto aninhado, e não só o identificador: a listagem da agenda desenha
    /// o nome e o ícone de cada linha, e devolver só o <c>Guid</c> obrigaria o
    /// cliente a cruzar com a lista de tipos a cada render — ou a fazer uma
    /// segunda requisição para mostrar uma palavra.
    /// </remarks>
    public EventTypeRef? Type { get; init; }

    /// <summary>
    /// Endereço do evento, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Convive com <see cref="Location"/>: aquele é o nome do lugar como a
    /// igreja o chama ("Templo", "Chácara do irmão João"), este é onde fica.
    /// </remarks>
    public AddressResponse? Address { get; init; }
}

public sealed record SaveEventRequest
{
    [Required, MaxLength(200), MinLength(2)]
    public required string Title { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    [MaxLength(200)]
    public string? Location { get; init; }

    public required DateTimeOffset StartsAt { get; init; }
    public required DateTimeOffset EndsAt { get; init; }

    /// <summary>
    /// Identificador público do tipo, ou <c>null</c> para evento sem
    /// classificação.
    /// </summary>
    /// <remarks>
    /// Ausente não afirma nada: um evento que ninguém classificou fica sem
    /// tipo, que é mais honesto do que atribuir-lhe um rótulo genérico. Na
    /// edição, <c>null</c> significa "não mexa no tipo" — para <b>remover</b> a
    /// classificação existe <see cref="ClearType"/>, porque sem essa distinção
    /// os dois pedidos chegariam idênticos ao servidor.
    /// </remarks>
    public Guid? TypeId { get; init; }

    /// <summary>Remove a classificação do evento. Só faz sentido na edição.</summary>
    public bool ClearType { get; init; }

    /// <summary>
    /// Endereço do evento.
    /// </summary>
    /// <remarks>
    /// Ausente na edição significa "não mexa no endereço"; um objeto com todos
    /// os campos em branco significa "apague o endereço". É a mesma convenção do
    /// endereço do membro, e o motivo é o mesmo: quem edita só o horário não
    /// deve perder o endereço por omissão.
    /// </remarks>
    public AddressPayload? Address { get; init; }
}

public static class EventEndpoints
{
    /// <summary>
    /// Teto da janela consultável. Sem ele, <c>from=1900&amp;to=2100</c> traria a
    /// agenda inteira em uma resposta — o mesmo raciocínio do teto de
    /// <c>pageSize</c> nas listagens paginadas.
    /// </summary>
    private const int MaxJanelaEmDias = 400;

    /// <summary>
    /// Resolve o <c>public_id</c> do tipo para a chave interna.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Devolve <c>404</c> quando o tipo não existe — e aqui a recusa é certa, ao
    /// contrário do enum que este campo substituiu: um identificador que o
    /// cliente inventou não é "classificação desconhecida", é referência
    /// quebrada, e aceitá-la em silêncio gravaria o evento sem o tipo que o
    /// usuário escolheu, sem ninguém perceber.
    /// </para>
    /// <para>
    /// O tipo de outra igreja também cai aqui: o RLS não o devolve, então a
    /// busca não encontra. É o isolamento fazendo o trabalho, sem um
    /// <c>WHERE tenant_id</c> escrito à mão que alguém poderia esquecer.
    /// </para>
    /// </remarks>
    private static async Task<(long? TypeId, IResult? Erro)> ResolverTipoAsync(
        Guid? publicId,
        IEventTypeRepository types,
        CancellationToken cancellationToken)
    {
        if (publicId is not { } id)
        {
            return (null, null);
        }

        var tipo = await types.FindByPublicIdAsync(id, cancellationToken);

        if (tipo is null)
        {
            return (null, TypedResults.Problem(
                title: "Tipo não encontrado",
                detail: "O tipo informado não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound));
        }

        return (tipo.Id, null);
    }

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events").WithTags("Agenda");

        // Leitura para qualquer membro da igreja: a agenda é informação da
        // congregação. Escrita exige events.write.
        group.MapGet("/", ListAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithSummary("Lista eventos que acontecem numa janela de datas");

        group.MapGet("/upcoming", ListUpcomingAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithSummary("Próximos eventos, para o painel de início");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithSummary("Detalha um evento");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Agenda um evento");

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Edita título, descrição, local e horário");

        group.MapPut("/{id:guid}/cancel", CancelAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Cancela sem apagar — o evento continua visível, marcado");

        group.MapPut("/{id:guid}/reactivate", ReactivateAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Desfaz o cancelamento");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Apaga um evento criado por engano");
    }

    private static async Task<IResult> ListAsync(
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] bool includeCanceled = true)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (from is not { } inicio || to is not { } fim)
        {
            return TypedResults.Problem(
                title: "Janela obrigatória",
                detail: "Informe 'from' e 'to' — a agenda é sempre consultada por período.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (fim <= inicio)
        {
            return TypedResults.Problem(
                title: "Janela inválida",
                detail: "O fim da janela precisa ser depois do começo.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if ((fim - inicio).TotalDays > MaxJanelaEmDias)
        {
            return TypedResults.Problem(
                title: "Janela muito longa",
                detail: $"Consulte no máximo {MaxJanelaEmDias} dias por vez.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var itens = await events.ListAsync(
            new EventQuery { From = inicio, To = fim, IncludeCanceled = includeCanceled },
            cancellationToken);

        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync(itens, addresses, cancellationToken);

        return TypedResults.Ok(itens.Select(e => ToResponse(e, tipos, enderecos)).ToList());
    }

    private static async Task<IResult> ListUpcomingAsync(
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 5)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await events.ListUpcomingAsync(timeProvider.GetUtcNow(), limit, cancellationToken);
        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync(itens, addresses, cancellationToken);

        return TypedResults.Ok(itens.Select(e => ToResponse(e, tipos, enderecos)).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var evento = await events.FindByPublicIdAsync(id, cancellationToken);

        if (evento is null)
        {
            return EventNotFound();
        }

        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync([evento], addresses, cancellationToken);

        return TypedResults.Ok(ToResponse(evento, tipos, enderecos));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] SaveEventRequest request,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        var (typeId, erroDeTipo) = await ResolverTipoAsync(request.TypeId, types, cancellationToken);
        if (erroDeTipo is not null)
        {
            return erroDeTipo;
        }

        long? addressId;
        try
        {
            addressId = await request.Address.AplicarAsync(
                atualId: null, tenantId, addresses, unitOfWork, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Endereço inválido",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        CalendarEvent evento;
        try
        {
            evento = CalendarEvent.Schedule(
                tenantId: tenantId,
                title: request.Title,
                startsAt: request.StartsAt,
                endsAt: request.EndsAt,
                now: timeProvider.GetUtcNow(),
                description: request.Description,
                location: request.Location,
                typeId: typeId,
                addressId: addressId);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        events.Add(evento);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync([evento], addresses, cancellationToken);

        return TypedResults.Created(
            $"/api/v1/events/{evento.PublicId}",
            ToResponse(evento, tipos, enderecos));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SaveEventRequest request,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var evento = await events.FindByPublicIdAsync(id, cancellationToken);

        if (evento is null)
        {
            return EventNotFound();
        }

        var (typeId, erroDeTipo) = await ResolverTipoAsync(request.TypeId, types, cancellationToken);
        if (erroDeTipo is not null)
        {
            return erroDeTipo;
        }

        long? addressId;
        try
        {
            addressId = await request.Address.AplicarAsync(
                evento.AddressId, evento.TenantId, addresses, unitOfWork, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Endereço inválido",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            evento.Update(
                title: request.Title,
                startsAt: request.StartsAt,
                endsAt: request.EndsAt,
                now: timeProvider.GetUtcNow(),
                description: request.Description,
                location: request.Location,
                typeId: typeId,
                clearType: request.ClearType,
                addressId: addressId,
                // Sem endereço no corpo E sem endereço resolvido significa que a
                // interface mandou apagar. `AplicarAsync` já devolveu null nesse
                // caso; a bandeira é o que distingue isso de "não mexa".
                clearAddress: request.Address is not null && addressId is null);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync([evento], addresses, cancellationToken);

        return TypedResults.Ok(ToResponse(evento, tipos, enderecos));
    }

    private static Task<IResult> CancelAsync(
        Guid id,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MudarStatusAsync(id, events, types, addresses, unitOfWork, tenant, timeProvider, cancelar: true, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        Guid id,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MudarStatusAsync(id, events, types, addresses, unitOfWork, tenant, timeProvider, cancelar: false, cancellationToken);

    private static async Task<IResult> MudarStatusAsync(
        Guid id,
        IEventRepository events,
        IEventTypeRepository types,
        IAddressRepository addresses,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        bool cancelar,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var evento = await events.FindByPublicIdAsync(id, cancellationToken);

        if (evento is null)
        {
            return EventNotFound();
        }

        var agora = timeProvider.GetUtcNow();

        if (cancelar)
        {
            evento.Cancel(agora);
        }
        else
        {
            evento.Reactivate(agora);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tipos = await CarregarTiposAsync(types, cancellationToken);
        var enderecos = await CarregarEnderecosAsync([evento], addresses, cancellationToken);

        return TypedResults.Ok(ToResponse(evento, tipos, enderecos));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IEventRepository events,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var evento = await events.FindByPublicIdAsync(id, cancellationToken);

        if (evento is null)
        {
            return EventNotFound();
        }

        events.Remove(evento);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Nenhuma igreja selecionada",
            detail: "Esta área exige vínculo com uma igreja. Selecione uma igreja e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult EventNotFound() =>
        TypedResults.Problem(
            title: "Evento não encontrado",
            detail: "Este evento não existe ou não pertence à sua igreja.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Monta a resposta resolvendo o tipo pelo mapa carregado uma vez por
    /// requisição.
    /// </summary>
    /// <remarks>
    /// Um mapa, e não um <c>Include</c> por evento: a alternativa natural seria
    /// carregar a entidade do tipo junto de cada evento, o que faz a listagem de
    /// um mês materializar o mesmo tipo dezenas de vezes. São poucos tipos por
    /// igreja; carregá-los todos de uma vez custa uma consulta e resolve todas
    /// as linhas.
    ///
    /// <para>
    /// O mapa inclui os <b>inativos</b>. Um culto de março continua classificado
    /// como "Culto" mesmo depois de alguém desativar o tipo — omiti-los faria a
    /// agenda histórica perder o rótulo de repente.
    /// </para>
    /// </remarks>
    private static EventResponse ToResponse(
        CalendarEvent evento,
        IReadOnlyDictionary<long, EventType> tipos,
        IReadOnlyDictionary<long, Address> enderecos) => new()
    {
        Id = evento.PublicId,
        Title = evento.Title,
        Description = evento.Description,
        Location = evento.Location,
        StartsAt = evento.StartsAt,
        EndsAt = evento.EndsAt,
        Status = evento.Status.ToString(),
        Type = evento.TypeId is { } id && tipos.TryGetValue(id, out var tipo)
            ? new EventTypeRef
            {
                Id = tipo.PublicId,
                Name = tipo.Name,
                Icon = tipo.Icon,
                ColorHex = tipo.ColorHex,
            }
            : null,
        Address = evento.AddressId is { } enderecoId && enderecos.TryGetValue(enderecoId, out var endereco)
            ? endereco.ToResponse()
            : null,
    };

    /// <summary>
    /// Endereços dos eventos da resposta, numa consulta só.
    /// </summary>
    /// <remarks>
    /// Mesmo raciocínio de <see cref="CarregarTiposAsync"/>: a alternativa seria
    /// uma consulta por evento, e uma agenda de um mês faria dezenas de idas ao
    /// banco para desenhar uma tela.
    /// </remarks>
    private static Task<IReadOnlyDictionary<long, Address>> CarregarEnderecosAsync(
        IEnumerable<CalendarEvent> eventos,
        IAddressRepository addresses,
        CancellationToken cancellationToken)
    {
        var ids = eventos
            .Where(e => e.AddressId is not null)
            .Select(e => e.AddressId!.Value)
            .Distinct()
            .ToList();

        return addresses.ListByIdsAsync(ids, cancellationToken);
    }

    /// <summary>
    /// Tipos da igreja indexados pela chave interna, inativos inclusive.
    /// </summary>
    private static async Task<IReadOnlyDictionary<long, EventType>> CarregarTiposAsync(
        IEventTypeRepository types,
        CancellationToken cancellationToken)
    {
        var itens = await types.ListAsync(includeInactive: true, cancellationToken);
        return itens.ToDictionary(t => t.Id);
    }
}
