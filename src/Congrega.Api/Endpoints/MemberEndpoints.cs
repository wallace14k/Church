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

/// <summary>
/// Campos editáveis pela ficha.
/// </summary>
/// <remarks>
/// Um subconjunto de <see cref="CreateMemberRequest"/> — gênero, estado civil,
/// data de vínculo e batismo ainda não têm campo na tela de edição. Adicionar
/// aqui sem a tela correspondente reabriria o mesmo problema que o `TODO.md`
/// já flagra: contrato que compila mas nunca é exercitado.
/// </remarks>
public sealed record UpdateMemberRequest
{
    [Required, MaxLength(200), MinLength(2)]
    public required string FullName { get; init; }

    [EmailAddress, MaxLength(254)]
    public string? Email { get; init; }

    [MaxLength(20)]
    public string? Phone { get; init; }

    public DateOnly? BirthDate { get; init; }

    [MaxLength(200)] public string? AddressStreet { get; init; }
    [MaxLength(20)]  public string? AddressNumber { get; init; }
    [MaxLength(100)] public string? AddressDistrict { get; init; }
    [MaxLength(100)] public string? AddressCity { get; init; }
    [MaxLength(2)]   public string? AddressState { get; init; }
    [MaxLength(9)]   public string? AddressZip { get; init; }
}

public sealed record ChangeMemberStatusRequest
{
    [Required]
    public required string Status { get; init; }
}

/// <summary>
/// <c>FamilyId</c> nulo desvincula o membro de qualquer família — é assim que a
/// tela remove alguém de um grupo, não com um endpoint separado de "remover".
/// </summary>
public sealed record AssignMemberFamilyRequest
{
    public Guid? FamilyId { get; init; }
}

/// <summary>
/// Uma linha da planilha, já mapeada para os campos do cadastro pela tela —
/// o backend não sabe (nem precisa saber) qual coluna original virou o quê.
/// </summary>
public sealed record ImportMemberRow
{
    [Required, MaxLength(200)]
    public required string FullName { get; init; }

    [MaxLength(254)]
    public string? Email { get; init; }

    [MaxLength(20)]
    public string? Phone { get; init; }

    public DateOnly? BirthDate { get; init; }

    [MaxLength(100)]
    public string? AddressCity { get; init; }
}

public sealed record ImportMembersRequest
{
    [Required]
    public required IReadOnlyList<ImportMemberRow> Rows { get; init; }
}

public sealed record ImportRowIssue
{
    /// <summary>Posição na lista enviada, 1-based — a mesma numeração que a tela mostrou ao usuário.</summary>
    public required int Row { get; init; }
    public required string Reason { get; init; }
}

public sealed record ImportMembersResponse
{
    public required int Imported { get; init; }
    public required int Skipped { get; init; }
    public required IReadOnlyList<ImportRowIssue> Issues { get; init; }
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

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Edita nome, contato, nascimento e endereço de um membro");

        group.MapPut("/{id:guid}/status", ChangeStatusAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Ativa, inativa ou marca transferido/falecido um membro");

        group.MapPut("/{id:guid}/family", AssignFamilyAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Vincula ou desvincula um membro de uma família");

        group.MapPost("/import", ImportAsync)
            .RequireAuthorization(Policies.MembersWrite)
            .WithSummary("Cadastra membros em lote, a partir de uma planilha mapeada pela tela");
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
        IFamilyRepository families,
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
        string? familyName = membro.FamilyId is { } familyId
            ? await families.FindNameByIdAsync(familyId, cancellationToken)
            : null;

        return TypedResults.Ok(new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(hoje),
            Status = membro.Status.ToString(),
            FamilyName = familyName,
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

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateMemberRequest request,
        IMemberRepository members,
        IFamilyRepository families,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var membro = await members.FindByPublicIdAsync(id, cancellationToken);

        // 404, não 403: mesmo raciocínio do GetAsync — o Global Query Filter já
        // esconde membro de outro tenant, e diferenciar aqui vazaria que o
        // identificador existe em algum lugar.
        if (membro is null)
        {
            return TypedResults.Problem(
                title: "Membro não encontrado",
                detail: "Este membro não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var agora = timeProvider.GetUtcNow();

        try
        {
            membro.UpdateProfile(
                fullName: request.FullName,
                email: request.Email,
                phone: request.Phone,
                birthDate: request.BirthDate,
                address: new Address
                {
                    Street = request.AddressStreet,
                    Number = request.AddressNumber,
                    District = request.AddressDistrict,
                    City = request.AddressCity,
                    State = request.AddressState?.ToUpperInvariant(),
                    ZipCode = request.AddressZip,
                },
                now: agora);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        string? familyName = membro.FamilyId is { } familyId
            ? await families.FindNameByIdAsync(familyId, cancellationToken)
            : null;

        return TypedResults.Ok(new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(DateOnly.FromDateTime(agora.UtcDateTime)),
            Status = membro.Status.ToString(),
            FamilyName = familyName,
        });
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        [FromBody] ChangeMemberStatusRequest request,
        IMemberRepository members,
        IFamilyRepository families,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        if (!Enum.TryParse<MemberStatus>(request.Status, ignoreCase: true, out var novoStatus))
        {
            return TypedResults.Problem(
                title: "Status inválido",
                detail: "Use Ativo, Inativo, Transferido ou Falecido.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var membro = await members.FindByPublicIdAsync(id, cancellationToken);

        if (membro is null)
        {
            return TypedResults.Problem(
                title: "Membro não encontrado",
                detail: "Este membro não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var agora = timeProvider.GetUtcNow();
        membro.ChangeStatus(novoStatus, agora);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        string? familyName = membro.FamilyId is { } familyId
            ? await families.FindNameByIdAsync(familyId, cancellationToken)
            : null;

        return TypedResults.Ok(new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(DateOnly.FromDateTime(agora.UtcDateTime)),
            Status = membro.Status.ToString(),
            FamilyName = familyName,
        });
    }

    private static async Task<IResult> AssignFamilyAsync(
        Guid id,
        [FromBody] AssignMemberFamilyRequest request,
        IMemberRepository members,
        IFamilyRepository families,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var membro = await members.FindByPublicIdAsync(id, cancellationToken);

        if (membro is null)
        {
            return TypedResults.Problem(
                title: "Membro não encontrado",
                detail: "Este membro não existe ou não pertence à sua igreja.",
                statusCode: StatusCodes.Status404NotFound);
        }

        long? familyId = null;
        string? familyName = null;

        if (request.FamilyId is { } familyPublicId)
        {
            var familia = await families.FindByPublicIdAsync(familyPublicId, cancellationToken);

            // 404, não 400: o mesmo raciocínio de posse dos demais endpoints —
            // uma família de outro tenant não deve nem confirmar que existe.
            if (familia is null)
            {
                return TypedResults.Problem(
                    title: "Família não encontrada",
                    detail: "Esta família não existe ou não pertence à sua igreja.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            familyId = familia.Id;
            familyName = familia.Name;
        }

        var agora = timeProvider.GetUtcNow();
        membro.AssignToFamily(familyId, agora);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new MemberResponse
        {
            Id = membro.PublicId,
            FullName = membro.FullName,
            Email = membro.Email,
            Phone = membro.Phone,
            BirthDate = membro.BirthDate,
            Age = membro.AgeOn(DateOnly.FromDateTime(agora.UtcDateTime)),
            Status = membro.Status.ToString(),
            FamilyName = familyName,
        });
    }

    /// <summary>
    /// Teto rígido de linhas por chamada — sem ele, uma planilha gigante vira
    /// negação de serviço de graça, o mesmo raciocínio do teto de <c>PageSize</c>
    /// em <see cref="MemberQuery"/>.
    /// </summary>
    private const int MaxImportRows = 500;

    private static async Task<IResult> ImportAsync(
        [FromBody] ImportMembersRequest request,
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

        if (request.Rows.Count == 0)
        {
            return TypedResults.Problem(
                title: "Nada para importar",
                detail: "Envie ao menos uma linha.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Rows.Count > MaxImportRows)
        {
            return TypedResults.Problem(
                title: "Lote muito grande",
                detail: $"Envie no máximo {MaxImportRows} linhas por vez.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var agora = timeProvider.GetUtcNow();

        // Uma consulta para todo o lote, não uma por linha — ver a nota em
        // IMemberRepository.ListEmailsAsync.
        var emailsExistentes = await members.ListEmailsAsync(cancellationToken);
        var emailsNesteLote = new HashSet<string>();

        var issues = new List<ImportRowIssue>();
        int importados = 0;

        for (int i = 0; i < request.Rows.Count; i++)
        {
            int linha = i + 1;
            var dados = request.Rows[i];

            string? emailNormalizado = null;
            if (!string.IsNullOrWhiteSpace(dados.Email))
            {
                emailNormalizado = dados.Email.Trim().ToLowerInvariant();

                if (emailsExistentes.Contains(emailNormalizado) || emailsNesteLote.Contains(emailNormalizado))
                {
                    issues.Add(new ImportRowIssue { Row = linha, Reason = "E-mail já cadastrado" });
                    continue;
                }
            }

            try
            {
                var membro = Member.Register(
                    tenantId: tenantId,
                    fullName: dados.FullName,
                    now: agora,
                    email: dados.Email,
                    phone: dados.Phone,
                    birthDate: dados.BirthDate,
                    address: dados.AddressCity is not null ? new Address { City = dados.AddressCity } : null);

                members.Add(membro);
                importados++;

                if (emailNormalizado is not null)
                {
                    emailsNesteLote.Add(emailNormalizado);
                }
            }
            catch (ArgumentException ex)
            {
                // Mesma regra do domínio que rejeita um cadastro avulso — nome
                // vazio, nascimento futuro — rejeita aqui, com a mesma mensagem.
                issues.Add(new ImportRowIssue { Row = linha, Reason = ex.Message });
            }
        }

        if (importados > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new ImportMembersResponse
        {
            Imported = importados,
            Skipped = issues.Count,
            Issues = issues,
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
