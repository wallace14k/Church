using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Calendar;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

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
}

public static class EventEndpoints
{
    /// <summary>
    /// Teto da janela consultável. Sem ele, <c>from=1900&amp;to=2100</c> traria a
    /// agenda inteira em uma resposta — o mesmo raciocínio do teto de
    /// <c>pageSize</c> nas listagens paginadas.
    /// </summary>
    private const int MaxJanelaEmDias = 400;

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

        return TypedResults.Ok(itens.Select(ToResponse).ToList());
    }

    private static async Task<IResult> ListUpcomingAsync(
        IEventRepository events,
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

        return TypedResults.Ok(itens.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IEventRepository events,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var evento = await events.FindByPublicIdAsync(id, cancellationToken);

        return evento is null ? EventNotFound() : TypedResults.Ok(ToResponse(evento));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] SaveEventRequest request,
        IEventRepository events,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
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
                location: request.Location);
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

        return TypedResults.Created($"/api/v1/events/{evento.PublicId}", ToResponse(evento));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SaveEventRequest request,
        IEventRepository events,
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

        try
        {
            evento.Update(
                title: request.Title,
                startsAt: request.StartsAt,
                endsAt: request.EndsAt,
                now: timeProvider.GetUtcNow(),
                description: request.Description,
                location: request.Location);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(evento));
    }

    private static Task<IResult> CancelAsync(
        Guid id,
        IEventRepository events,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MudarStatusAsync(id, events, unitOfWork, tenant, timeProvider, cancelar: true, cancellationToken);

    private static Task<IResult> ReactivateAsync(
        Guid id,
        IEventRepository events,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
        MudarStatusAsync(id, events, unitOfWork, tenant, timeProvider, cancelar: false, cancellationToken);

    private static async Task<IResult> MudarStatusAsync(
        Guid id,
        IEventRepository events,
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

        return TypedResults.Ok(ToResponse(evento));
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

    private static EventResponse ToResponse(CalendarEvent evento) => new()
    {
        Id = evento.PublicId,
        Title = evento.Title,
        Description = evento.Description,
        Location = evento.Location,
        StartsAt = evento.StartsAt,
        EndsAt = evento.EndsAt,
        Status = evento.Status.ToString(),
    };
}
