using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Calendar;
using Congrega.Domain.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record EventTypeResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Icon { get; init; }

    /// <summary><c>#RRGGBB</c>, ou <c>null</c> para a cor padrão do sistema.</summary>
    public string? ColorHex { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>
    /// Quantos eventos usam este tipo.
    /// </summary>
    /// <remarks>
    /// A interface usa isto para avisar antes de alguém tentar excluir — não
    /// para decidir se pode. Quem decide é a FK <c>RESTRICT</c>; conferir a
    /// contagem e só então apagar seria uma janela de corrida entre a leitura e
    /// a exclusão.
    /// </remarks>
    public required int EventCount { get; init; }
}

public sealed record SaveEventTypeRequest
{
    [Required, MaxLength(EventType.MaxNameLength), MinLength(2)]
    public required string Name { get; init; }

    [MaxLength(EventType.MaxIconLength)]
    public string? Icon { get; init; }

    /// <summary>
    /// Cor do tipo em <c>#RRGGBB</c>. Ausente mantém a que já está gravada.
    /// </summary>
    /// <remarks>
    /// A cor é reforço visual na agenda — o nome do tipo aparece escrito ao lado
    /// dela em todo lugar. Nunca é ela sozinha que diz de que tipo é o evento.
    /// </remarks>
    [MaxLength(7)]
    public string? ColorHex { get; init; }

    /// <summary>Ausente vira ativo — é o estado de quem acabou de criar um tipo.</summary>
    public bool? IsActive { get; init; }
}

public static class EventTypeEndpoints
{
    /// <summary>
    /// Ícones que o aplicativo sabe desenhar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A validação vive aqui, e não no banco nem no domínio, porque a lista
    /// depende da fonte de ícones que o cliente embarca — muda de versão para
    /// versão do app, e um <c>CHECK</c> no schema viraria uma migration a cada
    /// ícone novo.
    /// </para>
    /// <para>
    /// Aceitar texto livre seria pior do que parece: o nome inválido só falharia
    /// na hora de renderizar, no aparelho do usuário, como um espaço em branco
    /// sem explicação. Recusar na escrita transforma isso num 400 legível.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> IconesAceitos = new(StringComparer.Ordinal)
    {
        "calendar", "heart", "users", "book-open", "music", "mic", "coffee",
        "sunrise", "moon", "star", "gift", "home", "map-pin", "award", "flag",
        "bookmark", "smile", "zap", "droplet", "feather",
    };

    public static void MapEventTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/event-types").WithTags("Agenda");

        // Leitura para qualquer membro: o formulário de evento precisa da lista,
        // e quem pode agendar não é necessariamente quem administra o
        // vocabulário. Escrita exige a mesma permissão de escrever na agenda.
        group.MapGet("/", ListAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithSummary("Lista os tipos de evento da igreja");

        group.MapGet("/icons", ListIcons)
            .RequireAuthorization(Policies.TenantMember)
            .WithSummary("Ícones disponíveis para um tipo de evento");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Cadastra um tipo de evento");

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Renomeia, troca o ícone ou ativa/desativa um tipo");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(Policies.EventsWrite)
            .WithSummary("Exclui um tipo que nunca foi usado");
    }

    /// <summary>
    /// A lista de ícones vem do servidor para o cliente não precisar duplicá-la.
    /// </summary>
    /// <remarks>
    /// Duas cópias divergiriam: o app ofereceria um ícone que a API recusa, e o
    /// usuário levaria um 400 depois de escolher.
    /// </remarks>
    private static Ok<List<string>> ListIcons() => TypedResults.Ok(IconesAceitos.Order().ToList());

    private static async Task<IResult> ListAsync(
        IEventTypeRepository types,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await types.ListAsync(includeInactive, cancellationToken);
        var usos = await types.CountEventsByTypeAsync(cancellationToken);

        return TypedResults.Ok(itens.Select(t => ToResponse(t, usos)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] SaveEventTypeRequest request,
        IEventTypeRepository types,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        if (IconeInvalido(request.Icon) is { } problema)
        {
            return problema;
        }

        EventType tipo;
        try
        {
            tipo = EventType.Register(
                tenantId, request.Name, request.Icon, timeProvider.GetUtcNow(), request.ColorHex);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        types.Add(tipo);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == "uq_event_types_tenant_nome")
        {
            // A constraint é quem decide, não um SELECT antes do INSERT: duas
            // requisições simultâneas com o mesmo nome passariam as duas pela
            // verificação prévia e gravariam as duas.
            return NomeDuplicado(request.Name);
        }

        return TypedResults.Created(
            $"/api/v1/event-types/{tipo.PublicId}",
            ToResponse(tipo, usos: null));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] SaveEventTypeRequest request,
        IEventTypeRepository types,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (IconeInvalido(request.Icon) is { } problema)
        {
            return problema;
        }

        var tipo = await types.FindByPublicIdAsync(id, cancellationToken);

        if (tipo is null)
        {
            return TipoNaoEncontrado();
        }

        try
        {
            tipo.Update(
                request.Name,
                request.Icon,
                request.IsActive ?? true,
                timeProvider.GetUtcNow(),
                // Cor ausente MANTÉM a atual: o formulário pode salvar só o
                // nome, e apagar a cor nesse caso desfaria uma escolha que
                // ninguém pediu para desfazer.
                request.ColorHex ?? tipo.ColorHex);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == "uq_event_types_tenant_nome")
        {
            return NomeDuplicado(request.Name);
        }

        var usos = await types.CountEventsByTypeAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(tipo, usos));
    }

    /// <summary>
    /// Exclui um tipo que nunca foi usado.
    /// </summary>
    /// <remarks>
    /// Tipo em uso responde <c>409</c> apontando para a desativação, e quem
    /// recusa é a FK <c>RESTRICT</c> — não uma contagem lida antes. Apagar
    /// "Culto" com duzentos cultos gravados destruiria a classificação de dois
    /// anos de agenda por um clique.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        IEventTypeRepository types,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var tipo = await types.FindByPublicIdAsync(id, cancellationToken);

        if (tipo is null)
        {
            return TipoNaoEncontrado();
        }

        types.Remove(tipo);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ForeignKeyViolationException)
        {
            return TypedResults.Problem(
                title: "Tipo em uso",
                detail: "Há eventos classificados com este tipo. Desative-o para tirá-lo do "
                      + "formulário sem apagar a classificação da agenda passada.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.NoContent();
    }

    private static ProblemHttpResult? IconeInvalido(string? icone)
    {
        if (string.IsNullOrWhiteSpace(icone) || IconesAceitos.Contains(icone.Trim()))
        {
            return null;
        }

        return TypedResults.Problem(
            title: "Ícone desconhecido",
            detail: "Escolha um dos ícones disponíveis em /api/v1/event-types/icons.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static ProblemHttpResult NomeDuplicado(string nome) =>
        TypedResults.Problem(
            title: "Nome já usado",
            detail: $"Já existe um tipo chamado \"{nome}\" nesta igreja.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Nenhuma igreja selecionada",
            detail: "Esta área exige vínculo com uma igreja. Selecione uma igreja e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult TipoNaoEncontrado() =>
        TypedResults.Problem(
            title: "Tipo não encontrado",
            detail: "Este tipo não existe ou não pertence à sua igreja.",
            statusCode: StatusCodes.Status404NotFound);

    private static EventTypeResponse ToResponse(EventType tipo, IReadOnlyDictionary<long, int>? usos) => new()
    {
        Id = tipo.PublicId,
        Name = tipo.Name,
        Icon = tipo.Icon,
        ColorHex = tipo.ColorHex,
        IsActive = tipo.IsActive,
        EventCount = usos is null ? 0 : usos.GetValueOrDefault(tipo.Id),
    };
}
