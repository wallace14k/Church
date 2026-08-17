using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Congregation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record FamilyResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int MemberCount { get; init; }
}

public sealed record FamilyMemberResponse
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public required string Status { get; init; }
}

public sealed record FamilyDetailResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<FamilyMemberResponse> Members { get; init; }
}

public sealed record CreateFamilyRequest
{
    [Required, MaxLength(200), MinLength(2)]
    public required string Name { get; init; }
}

public static class FamilyEndpoints
{
    public static void MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/families").WithTags("Famílias");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(Policies.MembersRead)
            .WithSummary("Lista famílias da igreja, com a contagem de membros de cada uma");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(Policies.MembersRead)
            .WithSummary("Detalha uma família e lista seus membros");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Cadastra uma família");
    }

    private static async Task<IResult> ListAsync(
        IFamilyRepository families,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await families.ListAsync(cancellationToken);

        return TypedResults.Ok(itens.Select(f => new FamilyResponse
        {
            Id = f.PublicId,
            Name = f.Name,
            MemberCount = f.MemberCount,
        }).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IFamilyRepository families,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var familia = await families.FindByPublicIdAsync(id, cancellationToken);

        if (familia is null)
        {
            return FamilyNotFound();
        }

        var membros = await families.ListMembersAsync(familia.Id, cancellationToken);

        return TypedResults.Ok(new FamilyDetailResponse
        {
            Id = familia.PublicId,
            Name = familia.Name,
            Members = membros.Select(m => new FamilyMemberResponse
            {
                Id = m.PublicId,
                FullName = m.FullName,
                Status = m.Status.ToString(),
            }).ToList(),
        });
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateFamilyRequest request,
        IFamilyRepository families,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        Family familia;
        try
        {
            familia = Family.Register(tenantId, request.Name, timeProvider.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        families.Add(familia);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/families/{familia.PublicId}", new FamilyResponse
        {
            Id = familia.PublicId,
            Name = familia.Name,
            MemberCount = 0,
        });
    }

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Nenhuma igreja selecionada",
            detail: "Esta área exige vínculo com uma igreja. Selecione uma igreja e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult FamilyNotFound() =>
        TypedResults.Problem(
            title: "Família não encontrada",
            detail: "Esta família não existe ou não pertence à sua igreja.",
            statusCode: StatusCodes.Status404NotFound);
}
