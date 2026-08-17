using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Common;
using Congrega.Domain.Congregation;
using Congrega.Domain.Giving;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record GivingCategoryResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    /// <summary>`Entrada` ou `Saida`.</summary>
    public required string Kind { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record CreateGivingCategoryRequest
{
    [Required, MaxLength(100), MinLength(2)]
    public required string Name { get; init; }

    [Required]
    public required string Kind { get; init; }
}

public sealed record UpdateGivingCategoryRequest
{
    [Required, MaxLength(100), MinLength(2)]
    public required string Name { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed record GivingEntryResponse
{
    public required Guid Id { get; init; }
    public required string CategoryName { get; init; }
    public required string Kind { get; init; }
    /// <summary>Centavos. Sempre positivo — o sinal vem de <c>Kind</c>.</summary>
    public required long AmountCents { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required string Method { get; init; }
    public string? MemberName { get; init; }
    public string? Notes { get; init; }
}

public sealed record CreateGivingEntryRequest
{
    [Required]
    public required Guid CategoryId { get; init; }

    [Range(1, long.MaxValue)]
    public required long AmountCents { get; init; }

    [Required]
    public required DateOnly OccurredOn { get; init; }

    [Required]
    public required string Method { get; init; }

    public Guid? MemberId { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }
}

public sealed record ClosingLineResponse
{
    public required Guid CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required string Kind { get; init; }
    public required long TotalCents { get; init; }
    public required int EntryCount { get; init; }
}

public sealed record MonthlyClosingResponse
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required long TotalIncomeCents { get; init; }
    public required long TotalExpenseCents { get; init; }
    /// <summary>Entradas menos saídas. Negativo é informação, não erro.</summary>
    public required long BalanceCents { get; init; }
    public required IReadOnlyList<ClosingLineResponse> Lines { get; init; }
}

public static class GivingEndpoints
{
    public static void MapGivingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/giving").WithTags("Financeiro");

        group.MapGet("/categories", ListCategoriesAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Lista categorias de lançamento");

        group.MapPost("/categories", CreateCategoryAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Cadastra uma categoria de lançamento");

        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Renomeia, ativa ou desativa uma categoria");

        group.MapGet("/entries", ListEntriesAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Lista lançamentos do período");

        group.MapPost("/entries", CreateEntryAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Lança uma entrada ou saída de caixa");

        group.MapDelete("/entries/{id:guid}", DeleteEntryAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Apaga um lançamento digitado por engano");

        group.MapGet("/closing", GetClosingAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Fechamento do mês, somado por categoria");
    }

    private static async Task<IResult> ListCategoriesAsync(
        IGivingCategoryRepository categories,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await categories.ListAsync(includeInactive, cancellationToken);

        return TypedResults.Ok(itens.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateCategoryAsync(
        [FromBody] CreateGivingCategoryRequest request,
        IGivingCategoryRepository categories,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        if (!Enum.TryParse<GivingKind>(request.Kind, ignoreCase: true, out var kind))
        {
            return TypedResults.Problem(
                title: "Tipo inválido",
                detail: "Use Entrada ou Saida.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        GivingCategory categoria;
        try
        {
            categoria = GivingCategory.Register(tenantId, request.Name, kind, timeProvider.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        categories.Add(categoria);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A correção vem da constraint, não de um `if (!exists)` antes de
            // inserir — que seria race condition sob duas abas abertas.
            return TypedResults.Problem(
                title: "Categoria repetida",
                detail: "Já existe uma categoria com esse nome nesta igreja.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/api/v1/giving/categories/{categoria.PublicId}", ToResponse(categoria));
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid id,
        [FromBody] UpdateGivingCategoryRequest request,
        IGivingCategoryRepository categories,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var categoria = await categories.FindByPublicIdAsync(id, cancellationToken);

        if (categoria is null)
        {
            return CategoryNotFound();
        }

        var agora = timeProvider.GetUtcNow();

        try
        {
            categoria.Rename(request.Name, agora);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        categoria.SetActive(request.IsActive, agora);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return TypedResults.Problem(
                title: "Categoria repetida",
                detail: "Já existe uma categoria com esse nome nesta igreja.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(ToResponse(categoria));
    }

    private static async Task<IResult> ListEntriesAsync(
        IGivingEntryRepository entries,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (month is { } m && (m < 1 || m > 12))
        {
            return TypedResults.Problem(
                title: "Mês inválido",
                detail: "Informe um mês entre 1 e 12.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var resultado = await entries.ListAsync(
            new GivingEntryQuery
            {
                Year = year,
                Month = month,
                CategoryPublicId = categoryId,
                Page = page,
                PageSize = pageSize,
            },
            cancellationToken);

        return TypedResults.Ok(new PagedResponse<GivingEntryResponse>
        {
            Items = resultado.Items.Select(ToResponse).ToList(),
            Page = resultado.Page,
            PageSize = resultado.PageSize,
            TotalCount = resultado.TotalCount,
            TotalPages = resultado.TotalPages,
            HasNext = resultado.HasNext,
        });
    }

    private static async Task<IResult> CreateEntryAsync(
        [FromBody] CreateGivingEntryRequest request,
        IGivingEntryRepository entries,
        IGivingCategoryRepository categories,
        IMemberRepository members,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        if (!Enum.TryParse<GivingMethod>(request.Method, ignoreCase: true, out var metodo))
        {
            return TypedResults.Problem(
                title: "Forma de pagamento inválida",
                detail: "Use Dinheiro, Pix, Cartao, Transferencia, Cheque ou Outro.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var categoria = await categories.FindByPublicIdAsync(request.CategoryId, cancellationToken);

        if (categoria is null)
        {
            return CategoryNotFound();
        }

        if (!categoria.IsActive)
        {
            // 400 e não 404: a categoria existe e o usuário pode vê-la no
            // histórico. Esconder aqui confundiria mais do que explicar.
            return TypedResults.Problem(
                title: "Categoria desativada",
                detail: "Reative a categoria para lançar nela.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        long? memberId = null;
        string? memberName = null;
        if (request.MemberId is { } membroPublico)
        {
            var membro = await members.FindByPublicIdAsync(membroPublico, cancellationToken);

            if (membro is null)
            {
                return TypedResults.Problem(
                    title: "Membro não encontrado",
                    detail: "Este membro não existe ou não pertence à sua igreja.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            memberId = membro.Id;
            memberName = membro.FullName;
        }

        GivingEntry lancamento;
        try
        {
            lancamento = GivingEntry.Register(
                tenantId: tenantId,
                categoryId: categoria.Id,
                amountCents: request.AmountCents,
                occurredOn: request.OccurredOn,
                method: metodo,
                now: timeProvider.GetUtcNow(),
                memberId: memberId,
                notes: request.Notes,
                recordedByUserId: tenant.UserId);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        entries.Add(lancamento);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/giving/entries/{lancamento.PublicId}", new GivingEntryResponse
        {
            Id = lancamento.PublicId,
            CategoryName = categoria.Name,
            Kind = categoria.Kind.ToString(),
            AmountCents = lancamento.AmountCents,
            OccurredOn = lancamento.OccurredOn,
            Method = lancamento.Method.ToString(),
            // Preenchido a partir do membro já carregado acima. Devolver null
            // aqui faria a resposta da criação descrever como anônimo um
            // lançamento que tem doador — foi assim que o mesmo descuido passou
            // despercebido em `familyName` na ficha de membro.
            MemberName = memberName,
            Notes = lancamento.Notes,
        });
    }

    private static async Task<IResult> DeleteEntryAsync(
        Guid id,
        IGivingEntryRepository entries,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var lancamento = await entries.FindByPublicIdAsync(id, cancellationToken);

        if (lancamento is null)
        {
            return TypedResults.Problem(
                title: "Lançamento não encontrado",
                detail: "Este lançamento não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound);
        }

        entries.Remove(lancamento);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetClosingAsync(
        IGivingEntryRepository entries,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var hoje = timeProvider.GetUtcNow();
        int ano = year ?? hoje.Year;
        int mes = month ?? hoje.Month;

        if (mes is < 1 or > 12)
        {
            return TypedResults.Problem(
                title: "Mês inválido",
                detail: "Informe um mês entre 1 e 12.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var fechamento = await entries.SummarizeMonthAsync(ano, mes, cancellationToken);

        return TypedResults.Ok(new MonthlyClosingResponse
        {
            Year = fechamento.Year,
            Month = fechamento.Month,
            TotalIncomeCents = fechamento.TotalIncomeCents,
            TotalExpenseCents = fechamento.TotalExpenseCents,
            BalanceCents = fechamento.BalanceCents,
            Lines = fechamento.Lines.Select(l => new ClosingLineResponse
            {
                CategoryId = l.CategoryPublicId,
                CategoryName = l.CategoryName,
                Kind = l.Kind.ToString(),
                TotalCents = l.TotalCents,
                EntryCount = l.EntryCount,
            }).ToList(),
        });
    }

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Nenhuma igreja selecionada",
            detail: "Esta área exige vínculo com uma igreja. Selecione uma igreja e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult CategoryNotFound() =>
        TypedResults.Problem(
            title: "Categoria não encontrada",
            detail: "Esta categoria não existe ou não pertence à sua igreja.",
            statusCode: StatusCodes.Status404NotFound);

    private static GivingCategoryResponse ToResponse(GivingCategory categoria) => new()
    {
        Id = categoria.PublicId,
        Name = categoria.Name,
        Kind = categoria.Kind.ToString(),
        IsActive = categoria.IsActive,
    };

    private static GivingEntryResponse ToResponse(GivingEntryListItem item) => new()
    {
        Id = item.PublicId,
        CategoryName = item.CategoryName,
        Kind = item.Kind.ToString(),
        AmountCents = item.AmountCents,
        OccurredOn = item.OccurredOn,
        Method = item.Method.ToString(),
        MemberName = item.MemberName,
        Notes = item.Notes,
    };
}
