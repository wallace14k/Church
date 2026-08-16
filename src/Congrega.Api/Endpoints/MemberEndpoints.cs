using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Congregation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record MemberResponse
{
    public required Guid Id { get; init; }
    public required string FullName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public DateOnly? BirthDate { get; init; }
    public int? Age { get; init; }
    public required string Status { get; init; }
    public string? FamilyName { get; init; }
}

public sealed record PagedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required int TotalPages { get; init; }
    public required bool HasNext { get; init; }
}

public sealed record CreateMemberRequest
{
    [Required, MaxLength(200), MinLength(2)]
    public required string FullName { get; init; }

    [EmailAddress, MaxLength(254)]
    public string? Email { get; init; }

    [MaxLength(20)]
    public string? Phone { get; init; }

    public DateOnly? BirthDate { get; init; }
    public int? Gender { get; init; }
    public int? MaritalStatus { get; init; }
    public DateOnly? MembershipDate { get; init; }
    public DateOnly? BaptismDate { get; init; }

    [MaxLength(200)] public string? AddressStreet { get; init; }
    [MaxLength(20)]  public string? AddressNumber { get; init; }
    [MaxLength(100)] public string? AddressDistrict { get; init; }
    [MaxLength(100)] public string? AddressCity { get; init; }
    [MaxLength(2)]   public string? AddressState { get; init; }
    [MaxLength(9)]   public string? AddressZip { get; init; }

    [MaxLength(2000)] public string? Notes { get; init; }
}

public static class MemberEndpoints
{
    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/members").WithTags("Membros");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(Policies.MembersRead)
            .WithSummary("Lista membros da igreja");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(Policies.MembersRead)
            .WithSummary("Detalha um membro");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Cadastra um membro");
    }

    private static async Task<IResult> ListAsync(
        IMemberRepository members,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? birthdayMonth = null,
        [FromQuery] string? status = "Ativo")
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var resultado = await members.ListAsync(
            new MemberQuery
            {
                Search = search,
                Page = page,
                PageSize = pageSize,
                BirthdayMonth = birthdayMonth,
                Status = ParseStatus(status),
            },
            cancellationToken);

        return TypedResults.Ok(new PagedResponse<MemberResponse>
        {
            Items = resultado.Items.Select(ToResponse).ToList(),
            Page = resultado.Page,
            PageSize = resultado.PageSize,
            TotalCount = resultado.TotalCount,
            TotalPages = resultado.TotalPages,
            HasNext = resultado.HasNext,
        });
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IMemberRepository members,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var membro = await members.FindByPublicIdAsync(id, cancellationToken);

        // 404 e não 403 quando o membro é de outra igreja. O Global Query Filter
        // já o esconde, então chegamos aqui com null — e responder 403 confirmaria
        // que o identificador existe em algum lugar, que é vazamento por si só.
        if (membro is null)
        {
            return TypedResults.Problem(
                title: "Membro não encontrado",
                detail: "Este membro não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var hoje = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return TypedResults.Ok(new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(hoje),
            Status = membro.Status.ToString(),
        });
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateMemberRequest request,
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

        var agora = timeProvider.GetUtcNow();

        Member membro;
        try
        {
            membro = Member.Register(
                tenantId: tenantId,
                fullName: request.FullName,
                now: agora,
                email: request.Email,
                phone: request.Phone,
                birthDate: request.BirthDate,
                gender: (Gender?)request.Gender,
                maritalStatus: (MaritalStatus?)request.MaritalStatus,
                address: new Address
                {
                    Street = request.AddressStreet,
                    Number = request.AddressNumber,
                    District = request.AddressDistrict,
                    City = request.AddressCity,
                    State = request.AddressState?.ToUpperInvariant(),
                    ZipCode = request.AddressZip,
                },
                membershipDate: request.MembershipDate,
                baptismDate: request.BaptismDate,
                notes: request.Notes);
        }
        catch (ArgumentException ex)
        {
            // Invariante do domínio virando 400 com a mensagem do domínio. A regra
            // vive num lugar só, e a interface não a reimplementa.
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        members.Add(membro);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/members/{membro.PublicId}", new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(DateOnly.FromDateTime(agora.UtcDateTime)),
            Status = membro.Status.ToString(),
        });
    }

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Nenhuma igreja selecionada",
            detail: "Esta área exige vínculo com uma igreja. Selecione uma igreja e tente de novo.",
            statusCode: StatusCodes.Status409Conflict);

    private static MemberStatus? ParseStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("Todos", StringComparison.OrdinalIgnoreCase)
            ? null
            : Enum.TryParse<MemberStatus>(value, ignoreCase: true, out var status) ? status : MemberStatus.Ativo;

    private static MemberResponse ToResponse(MemberListItem item) => new()
    {
        Id = item.PublicId,
        FullName = item.FullName,
        Email = item.Email,
        Phone = item.Phone,
        BirthDate = item.BirthDate,
        Age = item.Age,
        Status = item.Status.ToString(),
        FamilyName = item.FamilyName,
    };
}
