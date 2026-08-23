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

    /// <summary><c>#RRGGBB</c>, ou <c>null</c> para a cor padrão do sistema.</summary>
    public string? ColorHex { get; init; }
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

    /// <summary>
    /// Título do lançamento, ou <c>null</c> nos gravados antes do campo existir.
    /// </summary>
    /// <remarks>
    /// Quem desenha cai no nome da categoria quando falta. Preencher aqui com o
    /// nome dela produziria a repetição ("Dízimo / Categoria: Dízimo") que este
    /// campo veio resolver.
    /// </remarks>
    public string? Description { get; init; }

    public required Guid CategoryId { get; init; }
    public required string CategoryName { get; init; }

    /// <summary><c>#RRGGBB</c>, ou <c>null</c> para a cor padrão do sistema.</summary>
    public string? CategoryColorHex { get; init; }

    /// <summary>
    /// <c>Entrada</c> ou <c>Saida</c> — do <b>lançamento</b>, não da categoria.
    /// </summary>
    /// <remarks>
    /// A distinção passou a importar quando a categoria ganhou <c>Ambos</c>: ler
    /// o sinal dela devolveria "ambos" para um lançamento que é definidamente
    /// uma coisa só.
    /// </remarks>
    public required string Kind { get; init; }

    /// <summary>Centavos. Sempre positivo — o sinal vem de <c>Kind</c>.</summary>
    public required long AmountCents { get; init; }

    public required DateOnly OccurredOn { get; init; }
    public required string Method { get; init; }
    public string? MemberName { get; init; }
    public string? AccountName { get; init; }
    public required bool IsRecurring { get; init; }

    /// <summary><c>Semanal</c>, <c>Mensal</c> ou <c>Anual</c>. Só com <c>isRecurring</c>.</summary>
    public string? Recurrence { get; init; }

    /// <summary><c>Realizado</c> ou <c>Previsto</c>. Só o realizado soma no fechamento.</summary>
    public required string Status { get; init; }

    /// <summary>Identidade da série periódica, quando pertence a uma.</summary>
    public Guid? SeriesId { get; init; }

    /// <summary>
    /// Quantas parcelas futuras foram criadas junto.
    /// </summary>
    /// <remarks>
    /// Só vem preenchido na <b>criação</b> de uma série — é o que permite a tela
    /// dizer "e mais 12 parcelas previstas" em vez de o usuário descobrir
    /// sozinho ao trocar de mês.
    /// </remarks>
    public int? GeneratedCount { get; init; }

    public string? Notes { get; init; }
}

/// <summary>Contagens do mês, para os chips de filtro da listagem.</summary>
public sealed record GivingSummaryResponse
{
    public required int Total { get; init; }
    public required int Entradas { get; init; }
    public required int Saidas { get; init; }

    /// <summary>Quantos lançamentos por categoria, indexado pelo id público.</summary>
    public required IReadOnlyDictionary<Guid, int> PorCategoria { get; init; }
}

public sealed record FinancialAccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    /// <summary><c>Caixa</c> ou <c>ContaBancaria</c>.</summary>
    public required string Kind { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record SaveFinancialAccountRequest
{
    [Required, MaxLength(FinancialAccount.MaxNameLength), MinLength(2)]
    public required string Name { get; init; }

    /// <summary>Ausente vira <c>Caixa</c> — o caso da igreja sem conta bancária.</summary>
    [MaxLength(20)]
    public string? Kind { get; init; }
}

public sealed record CreateGivingEntryRequest
{
    [Required]
    public required Guid CategoryId { get; init; }

    /// <summary>
    /// <c>Entrada</c> ou <c>Saida</c>. <b>Obrigatório.</b>
    /// </summary>
    /// <remarks>
    /// Não é derivado da categoria: uma categoria <c>Ambos</c> não tem sinal
    /// para emprestar. A compatibilidade entre os dois é verificada no servidor
    /// — lançar saída em "Dízimo" continua sendo erro, só que agora é recusado
    /// explicitamente em vez de virar entrada em silêncio.
    /// </remarks>
    [Required]
    public required string Kind { get; init; }

    [Range(1, long.MaxValue)]
    public required long AmountCents { get; init; }

    [Required]
    public required DateOnly OccurredOn { get; init; }

    [Required]
    public required string Method { get; init; }

    /// <summary>Título — "Aluguel", "Oferta do culto". Opcional.</summary>
    [MaxLength(GivingEntry.MaxDescriptionLength)]
    public string? Description { get; init; }

    public Guid? MemberId { get; init; }

    /// <summary>Conta ou caixa. Opcional: igreja com caixa único não precisa cadastrar conta.</summary>
    public Guid? AccountId { get; init; }

    /// <summary>
    /// <c>Semanal</c>, <c>Mensal</c> ou <c>Anual</c>. Ausente = não recorrente.
    /// </summary>
    /// <remarks>
    /// Um campo só, e não um par <c>recorrente</c> + <c>frequencia</c>: o par
    /// admite o estado inconsistente "recorrente sem frequência", que a
    /// constraint do banco recusaria com um erro que não diz nada. A frequência
    /// é o que define a recorrência.
    /// </remarks>
    [MaxLength(20)]
    public string? Recurrence { get; init; }

    [MaxLength(GivingEntry.MaxNotesLength)]
    public string? Notes { get; init; }
}

/// <summary>O anexo, sem os bytes — o que a tela de detalhe precisa saber.</summary>
public sealed record FiscalDocumentFileResponse
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required int SizeBytes { get; init; }
}

/// <summary>O documento fiscal de uma saída.</summary>
public sealed record FiscalDocumentResponse
{
    public required Guid Id { get; init; }

    /// <summary><c>NotaFiscal</c>, <c>NotaDeServico</c>, <c>CupomFiscal</c> ou <c>Recibo</c>.</summary>
    public required string DocumentType { get; init; }

    public string? Number { get; init; }
    public string? Series { get; init; }

    /// <summary>CPF ou CNPJ do emissor, <b>só dígitos</b>. Quem exibe formata.</summary>
    public string? IssuerTaxId { get; init; }

    public string? AccessKey { get; init; }

    /// <summary><c>null</c> quando o documento foi registrado sem anexo.</summary>
    public FiscalDocumentFileResponse? File { get; init; }
}

/// <summary>
/// O lançamento inteiro, para a tela de detalhe.
/// </summary>
/// <remarks>
/// Contrato separado do da listagem de propósito: a listagem desenha trinta
/// linhas e não deve carregar autor nem documento, e o detalhe mostra um
/// lançamento e precisa dos dois.
/// </remarks>
public sealed record GivingEntryDetailResponse
{
    public required Guid Id { get; init; }
    public string? Description { get; init; }
    public required Guid CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public string? CategoryColorHex { get; init; }
    public required string Kind { get; init; }
    public required long AmountCents { get; init; }
    public required DateOnly OccurredOn { get; init; }
    public required string Method { get; init; }
    public string? MemberName { get; init; }
    public string? AccountName { get; init; }
    public required bool IsRecurring { get; init; }
    public string? Recurrence { get; init; }
    public required string Status { get; init; }
    public Guid? SeriesId { get; init; }
    public string? Notes { get; init; }

    /// <summary>Quem digitou. Prestação de contas precisa da autoria.</summary>
    public string? RecordedByName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Sempre <c>null</c> em entrada — a igreja não emite nota ao receber oferta.</summary>
    public FiscalDocumentResponse? Document { get; init; }
}

/// <summary>O arquivo do comprovante, em Base64.</summary>
/// <remarks>
/// Base64 porque JSON não carrega binário. <b>O banco guarda os bytes</b> — a
/// tradução acontece aqui, na borda, e não na persistência: gravar o texto
/// Base64 custaria 33% a mais de disco em cada comprovante.
/// </remarks>
public sealed record FiscalDocumentFileContentResponse
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required string ContentBase64 { get; init; }
}

/// <summary>O arquivo enviado pelo cliente, em Base64.</summary>
public sealed record AttachmentRequest
{
    [Required, MaxLength(FiscalDocumentFile.MaxFileNameLength)]
    public required string FileName { get; init; }

    /// <summary><c>application/pdf</c>, <c>image/png</c> ou <c>image/jpeg</c>.</summary>
    [Required, MaxLength(100)]
    public required string ContentType { get; init; }

    /// <summary>Conteúdo do arquivo em Base64, sem o prefixo <c>data:</c>.</summary>
    [Required]
    public required string ContentBase64 { get; init; }
}

public sealed record CreateFiscalDocumentRequest
{
    /// <summary><c>NotaFiscal</c>, <c>NotaDeServico</c>, <c>CupomFiscal</c> ou <c>Recibo</c>.</summary>
    [Required, MaxLength(30)]
    public required string DocumentType { get; init; }

    /// <summary>Obrigatório, exceto em <c>Recibo</c> — recibo à mão costuma não ter número.</summary>
    [MaxLength(FiscalDocument.MaxNumberLength)]
    public string? Number { get; init; }

    [MaxLength(FiscalDocument.MaxSeriesLength)]
    public string? Series { get; init; }

    /// <summary>CPF ou CNPJ do emissor. A pontuação é aceita e descartada.</summary>
    [MaxLength(20)]
    public string? IssuerTaxId { get; init; }

    /// <summary>44 dígitos da NF-e/NFC-e. Espaços são aceitos e descartados.</summary>
    [MaxLength(60)]
    public string? AccessKey { get; init; }

    public AttachmentRequest? Attachment { get; init; }
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

    /// <summary>
    /// Entradas ainda previstas — dinheiro que <b>não</b> se moveu.
    /// </summary>
    /// <remarks>
    /// Fora de <c>totalIncomeCents</c>, e devolvido ao lado dele. Somar os dois
    /// faria o fechamento afirmar um pagamento que não aconteceu; omitir o
    /// previsto faz a tela mostrar R$ 0,00 ao lado de uma lista com lançamentos,
    /// e quem lê conclui que o sistema perdeu a conta.
    /// </remarks>
    public required long PlannedIncomeCents { get; init; }

    /// <summary>Saídas ainda previstas.</summary>
    public required long PlannedExpenseCents { get; init; }

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

        group.MapGet("/entries/{id:guid}", GetEntryDetailAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Um lançamento com todos os detalhes e o documento fiscal");

        group.MapPost("/entries/{id:guid}/confirm", ConfirmEntryAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Confirma um lançamento previsto: o dinheiro se moveu");

        group.MapPost("/entries/{id:guid}/document", AttachDocumentAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Anexa o documento fiscal de uma saída");

        group.MapGet("/entries/{id:guid}/document/file", GetDocumentFileAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Baixa o comprovante anexado, em Base64");

        group.MapGet("/entries/summary", EntriesSummaryAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Contagens do mês para os filtros da listagem");

        group.MapGet("/accounts", ListAccountsAsync)
            .RequireAuthorization(Policies.GivingRead)
            .WithSummary("Lista as contas e caixas da igreja");

        group.MapPost("/accounts", CreateAccountAsync)
            .RequireAuthorization(Policies.GivingWrite)
            .WithSummary("Cadastra uma conta ou caixa");

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
        [FromQuery] string? kind = null,
        [FromQuery] string? search = null,
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
                // Rótulo desconhecido vira "sem filtro" em vez de 400: um link
                // antigo colado no navegador deve devolver a listagem, não uma
                // tela de erro.
                Kind = Enum.TryParse<GivingKind>(kind, ignoreCase: true, out var tipoFiltro)
                    && tipoFiltro is GivingKind.Entrada or GivingKind.Saida
                        ? tipoFiltro
                        : null,
                Search = search,
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

    /// <summary>
    /// Confirma um lançamento previsto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sem isto, uma série periódica é um beco sem saída.</b> As doze parcelas
    /// nascem <c>Previsto</c> justamente para não afirmarem um pagamento que não
    /// aconteceu — e, sem uma forma de confirmá-las, elas nunca entram em
    /// fechamento nenhum. O aluguel de setembro seria pago no mundo real e
    /// continuaria valendo R$ 0,00 no caixa, para sempre.
    /// </para>
    /// <para>
    /// A regra de "não confirmar o futuro" mora no domínio, onde a data é
    /// comparada com o relógio injetado. Confirmar o que já está realizado é
    /// silencioso, e não erro: dois cliques chegam ao mesmo estado.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ConfirmEntryAsync(
        Guid id,
        IGivingEntryRepository entries,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var lancamento = await entries.FindByPublicIdAsync(id, cancellationToken);

        if (lancamento is null)
        {
            return EntryNotFound();
        }

        try
        {
            lancamento.Confirmar(timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            // 409 e não 400: o pedido está bem formado, o estado é que não
            // permite ainda. A mensagem do domínio já diz o porquê.
            return TypedResults.Problem(
                title: "Ainda não dá para confirmar",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var detalhe = await entries.GetDetailAsync(id, cancellationToken);

        return detalhe is null ? EntryNotFound() : TypedResults.Ok(ToDetailResponse(detalhe));
    }

    private static async Task<IResult> GetEntryDetailAsync(
        Guid id,
        IGivingEntryRepository entries,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var detalhe = await entries.GetDetailAsync(id, cancellationToken);

        return detalhe is null ? EntryNotFound() : TypedResults.Ok(ToDetailResponse(detalhe));
    }

    /// <summary>
    /// Anexa o documento fiscal a uma saída já lançada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chamada própria, e não um bloco dentro de <c>POST /entries</c></b>, por
    /// duas razões concretas:
    /// </para>
    /// <para>
    /// A primeira é a rede. O comprovante chega a 10 MB — 13 MB depois do
    /// Base64. Se ele viajasse junto do lançamento, uma queda de conexão a 90%
    /// do envio perderia também o lançamento, e o tesoureiro teria de digitar
    /// tudo de novo por causa do anexo. Separados, o lançamento é salvo em
    /// milissegundos e só o arquivo é reenviado.
    /// </para>
    /// <para>
    /// A segunda é o mundo real: <b>a nota costuma chegar depois</b>. A despesa é
    /// lançada no dia do pagamento e o documento aparece dias mais tarde. Com o
    /// anexo numa chamada própria, isso é uma operação normal — e não um motivo
    /// para editar um lançamento já fechado.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AttachDocumentAsync(
        Guid id,
        [FromBody] CreateFiscalDocumentRequest request,
        IGivingEntryRepository entries,
        IFiscalDocumentRepository documents,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        if (!Enum.TryParse<FiscalDocumentType>(request.DocumentType, ignoreCase: true, out var tipo)
            || !Enum.IsDefined(tipo))
        {
            return TypedResults.Problem(
                title: "Tipo de documento inválido",
                detail: "Use NotaFiscal, NotaDeServico, CupomFiscal ou Recibo.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var lancamento = await entries.FindByPublicIdAsync(id, cancellationToken);

        if (lancamento is null)
        {
            return EntryNotFound();
        }

        // 400 e não 404: o lançamento existe e o usuário o está vendo. Dizer
        // "não encontrado" mandaria procurar um problema que não é esse.
        if (lancamento.Kind != GivingKind.Saida)
        {
            return TypedResults.Problem(
                title: "Documento fiscal só existe em saída",
                detail: "A igreja não emite nota ao receber uma oferta ou um dízimo.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        byte[]? conteudo = null;
        if (request.Attachment is { } anexo)
        {
            // `TryFromBase64String` e não `FromBase64String`: Base64 malformado é
            // erro do cliente, e uma exceção não tratada viraria 500 — a página
            // de erro genérica em vez da mensagem que diz o que houve.
            var buffer = new byte[((anexo.ContentBase64.Length * 3) / 4) + 3];

            if (!Convert.TryFromBase64String(anexo.ContentBase64, buffer, out var escritos))
            {
                return TypedResults.Problem(
                    title: "Arquivo inválido",
                    detail: "O conteúdo do anexo não é Base64 válido.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            conteudo = buffer[..escritos];
        }

        FiscalDocument documento;
        FiscalDocumentFile? arquivo = null;

        try
        {
            documento = FiscalDocument.Register(
                tenantId: tenantId,
                entryId: lancamento.Id,
                entryKind: lancamento.Kind,
                documentType: tipo,
                now: timeProvider.GetUtcNow(),
                number: request.Number,
                series: request.Series,
                issuerTaxId: request.IssuerTaxId,
                accessKey: request.AccessKey);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        documents.Add(documento);

        // O documento precisa de `Id` antes de o arquivo poder apontar para ele,
        // e o `Id` só existe depois da gravação — a coluna é IDENTITY.
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // 409: já existe documento para este lançamento. É a constraint
            // recusando, e não um `if (!existe)` que sob dois cliques deixaria
            // passar os dois — e dois comprovantes na mesma despesa é o começo
            // de uma despesa prestada em dobro.
            return TypedResults.Problem(
                title: "Este lançamento já tem documento fiscal",
                detail: "Cada despesa tem um comprovante. Apague o lançamento e refaça-o se o documento estiver errado.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (conteudo is not null && request.Attachment is { } dados)
        {
            try
            {
                arquivo = FiscalDocumentFile.Register(
                    tenantId: tenantId,
                    documentId: documento.Id,
                    fileName: dados.FileName,
                    contentType: dados.ContentType,
                    content: conteudo,
                    now: timeProvider.GetUtcNow());
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(
                    title: "Anexo inválido",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            documents.AddFile(arquivo);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Created(
            $"/api/v1/giving/entries/{id}",
            new FiscalDocumentResponse
            {
                Id = documento.PublicId,
                DocumentType = documento.DocumentType.ToString(),
                Number = documento.Number,
                Series = documento.Series,
                IssuerTaxId = documento.IssuerTaxId,
                AccessKey = documento.AccessKey,
                File = arquivo is null ? null : new FiscalDocumentFileResponse
                {
                    Id = arquivo.PublicId,
                    FileName = arquivo.FileName,
                    ContentType = arquivo.ContentType,
                    SizeBytes = arquivo.SizeBytes,
                },
            });
    }

    /// <summary>
    /// Devolve o comprovante em Base64.
    /// </summary>
    /// <remarks>
    /// JSON com Base64, e não `application/octet-stream`: a tela exibe o PDF ou
    /// a imagem embutidos, e uma URI `data:` é o que ela precisa. Um download
    /// binário obrigaria o cliente a montar um `blob` só para poder mostrar o
    /// que já tinha em mãos.
    /// </remarks>
    private static async Task<IResult> GetDocumentFileAsync(
        Guid id,
        IFiscalDocumentRepository documents,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var arquivo = await documents.FindFileByEntryPublicIdAsync(id, cancellationToken);

        if (arquivo is null)
        {
            return TypedResults.Problem(
                title: "Comprovante não encontrado",
                detail: "Este lançamento não tem arquivo anexado.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(new FiscalDocumentFileContentResponse
        {
            FileName = arquivo.FileName,
            ContentType = arquivo.ContentType,
            ContentBase64 = Convert.ToBase64String(arquivo.Content),
        });
    }

    private static GivingEntryDetailResponse ToDetailResponse(GivingEntryDetail d) =>
        new()
        {
            Id = d.PublicId,
            Description = d.Description,
            CategoryId = d.CategoryPublicId,
            CategoryName = d.CategoryName,
            CategoryColorHex = d.CategoryColorHex,
            Kind = d.Kind.ToString(),
            AmountCents = d.AmountCents,
            OccurredOn = d.OccurredOn,
            Method = d.Method.ToString(),
            MemberName = d.MemberName,
            AccountName = d.AccountName,
            IsRecurring = d.IsRecurring,
            Recurrence = d.Recurrence?.ToString(),
            Status = d.Status.ToString(),
            SeriesId = d.SeriesId,
            Notes = d.Notes,
            RecordedByName = d.RecordedByName,
            CreatedAt = d.CreatedAt,
            Document = d.Document is null ? null : new FiscalDocumentResponse
            {
                Id = d.Document.PublicId,
                DocumentType = d.Document.DocumentType.ToString(),
                Number = d.Document.Number,
                Series = d.Document.Series,
                IssuerTaxId = d.Document.IssuerTaxId,
                AccessKey = d.Document.AccessKey,
                File = d.Document.File is null ? null : new FiscalDocumentFileResponse
                {
                    Id = d.Document.File.PublicId,
                    FileName = d.Document.File.FileName,
                    ContentType = d.Document.File.ContentType,
                    SizeBytes = d.Document.File.SizeBytes,
                },
            },
        };

    private static async Task<IResult> CreateEntryAsync(
        [FromBody] CreateGivingEntryRequest request,
        IGivingEntryRepository entries,
        IGivingCategoryRepository categories,
        IFinancialAccountRepository accounts,
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

        long? contaId = null;
        if (request.AccountId is { } contaPublica)
        {
            var conta = await accounts.FindByPublicIdAsync(contaPublica, cancellationToken);

            // 404 e não "ignora em silêncio": um identificador que o cliente
            // inventou é referência quebrada, e aceitá-la gravaria o lançamento
            // sem a conta que o usuário escolheu. Conta de outra igreja também
            // cai aqui — o RLS não a devolve, então a busca não encontra.
            if (conta is null)
            {
                return TypedResults.Problem(
                    title: "Conta não encontrada",
                    detail: "A conta informada não existe ou não pertence à sua igreja.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            contaId = conta.Id;
        }

        if (!Enum.TryParse<GivingKind>(request.Kind, ignoreCase: true, out var tipo)
            || tipo is not (GivingKind.Entrada or GivingKind.Saida))
        {
            return TypedResults.Problem(
                title: "Tipo inválido",
                detail: "Informe \"Entrada\" ou \"Saida\".",
                statusCode: StatusCodes.Status400BadRequest);
        }

        GivingRecurrence? recorrencia = null;
        if (!string.IsNullOrWhiteSpace(request.Recurrence))
        {
            if (!Enum.TryParse<GivingRecurrence>(request.Recurrence, ignoreCase: true, out var lida))
            {
                return TypedResults.Problem(
                    title: "Frequência inválida",
                    detail: "Informe \"Semanal\", \"Mensal\" ou \"Anual\".",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            recorrencia = lida;
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

        // A categoria vira restrição, não fonte do sinal.
        //
        // Lançar saída em "Dízimo" sempre foi errado; antes o sistema o
        // convertia em entrada em silêncio, porque o sinal vinha da categoria.
        // Agora o erro é recusado com o motivo escrito.
        if (!categoria.Aceita(tipo))
        {
            return TypedResults.Problem(
                title: "Categoria incompatível",
                detail: $"A categoria \"{categoria.Name}\" não aceita lançamento de {tipo.ToString().ToLowerInvariant()}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // A identidade da série é criada ANTES do lançamento original, e
        // compartilhada por ele e por todas as parcelas.
        //
        // Sem isso o original ficaria fora da própria série: editar "o aluguel"
        // acertaria as doze parcelas e deixaria a primeira para trás — e o
        // índice único que protege contra parcela duplicada é PARCIAL
        // (), então não protegeria nada.
        Guid? serieId = recorrencia is null ? null : Guid.NewGuid();

        GivingEntry lancamento;
        try
        {
            lancamento = GivingEntry.Register(
                tenantId: tenantId,
                categoryId: categoria.Id,
                kind: tipo,
                amountCents: request.AmountCents,
                occurredOn: request.OccurredOn,
                method: metodo,
                now: timeProvider.GetUtcNow(),
                memberId: memberId,
                notes: request.Notes,
                recordedByUserId: tenant.UserId,
                description: request.Description,
                accountId: contaId,
                recurrence: recorrencia,
                seriesId: serieId);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        entries.Add(lancamento);

        // A série inteira entra na MESMA gravação do lançamento original.
        //
        // Uma transação, não treze: se a décima parcela falhasse numa gravação
        // separada, a igreja ficaria com uma série pela metade e nenhum sinal de
        // que faltou algo. Ou existe inteira, ou não existe.
        int geradas = 0;
        if (recorrencia is { } frequencia)
        {
            foreach (var data in GivingEntry.CalcularOcorrencias(request.OccurredOn, frequencia))
            {
                entries.Add(GivingEntry.Register(
                    tenantId: tenantId,
                    categoryId: categoria.Id,
                    kind: tipo,
                    amountCents: request.AmountCents,
                    occurredOn: data,
                    method: metodo,
                    now: timeProvider.GetUtcNow(),
                    memberId: memberId,
                    notes: request.Notes,
                    recordedByUserId: tenant.UserId,
                    description: request.Description,
                    accountId: contaId,
                    recurrence: frequencia,
                    // PREVISTO: a parcela tem data futura e não pode somar no
                    // fechamento até o dinheiro se mover de fato.
                    status: GivingEntryStatus.Previsto,
                    seriesId: serieId));

                geradas++;
            }
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == "uq_giving_entries_serie_data")
        {
            // Duas requisições gerando a mesma série ao mesmo tempo — clique
            // duplo, retry de rede. A constraint recusa a segunda em vez de
            // gravar uma parcela duplicada, que em livro-caixa é dinheiro que
            // não existe.
            return TypedResults.Problem(
                title: "Lançamento duplicado",
                detail: "Esta parcela da série já foi registrada.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/api/v1/giving/entries/{lancamento.PublicId}", new GivingEntryResponse
        {
            Id = lancamento.PublicId,
            Description = lancamento.Description,
            CategoryId = categoria.PublicId,
            CategoryName = categoria.Name,
            CategoryColorHex = categoria.ColorHex,
            Kind = lancamento.Kind.ToString(),
            AmountCents = lancamento.AmountCents,
            OccurredOn = lancamento.OccurredOn,
            Method = lancamento.Method.ToString(),
            MemberName = memberName,
            AccountName = null,
            IsRecurring = lancamento.IsRecurring,
            Recurrence = lancamento.Recurrence?.ToString(),
            Status = lancamento.Status.ToString(),
            SeriesId = lancamento.SeriesId,
            GeneratedCount = geradas,
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
            PlannedIncomeCents = fechamento.PlannedIncomeCents,
            PlannedExpenseCents = fechamento.PlannedExpenseCents,
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

    private static ProblemHttpResult EntryNotFound() =>
        TypedResults.Problem(
            title: "Lançamento não encontrado",
            detail: "Este lançamento não existe ou não pertence à sua igreja.",
            statusCode: StatusCodes.Status404NotFound);

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
        ColorHex = categoria.ColorHex,
        IsActive = categoria.IsActive,
    };

    private static FinancialAccountResponse ToResponse(FinancialAccount conta) => new()
    {
        Id = conta.PublicId,
        Name = conta.Name,
        Kind = conta.Kind.ToString(),
        IsActive = conta.IsActive,
    };

    private static GivingEntryResponse ToResponse(GivingEntryListItem item) => new()
    {
        Id = item.PublicId,
        Description = item.Description,
        CategoryId = item.CategoryPublicId,
        CategoryName = item.CategoryName,
        CategoryColorHex = item.CategoryColorHex,
        Kind = item.Kind.ToString(),
        AmountCents = item.AmountCents,
        OccurredOn = item.OccurredOn,
        Method = item.Method.ToString(),
        MemberName = item.MemberName,
        AccountName = item.AccountName,
        IsRecurring = item.IsRecurring,
        Recurrence = item.Recurrence?.ToString(),
        Status = item.Status.ToString(),
        SeriesId = item.SeriesId,
        Notes = item.Notes,
    };

    private static async Task<IResult> EntriesSummaryAsync(
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

        // Sem período informado, o mês corrente do SERVIDOR. Deixar o cliente
        // escolher faria dois usuários em fusos diferentes verem contagens
        // diferentes para a mesma igreja.
        var agora = timeProvider.GetUtcNow();
        int ano = year ?? agora.Year;
        int mes = month ?? agora.Month;

        if (mes is < 1 or > 12)
        {
            return TypedResults.Problem(
                title: "Mês inválido",
                detail: "Informe um mês entre 1 e 12.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var resumo = await entries.GetSummaryAsync(ano, mes, cancellationToken);

        return TypedResults.Ok(new GivingSummaryResponse
        {
            Total = resumo.Total,
            Entradas = resumo.Entradas,
            Saidas = resumo.Saidas,
            PorCategoria = resumo.PorCategoria,
        });
    }

    private static async Task<IResult> ListAccountsAsync(
        IFinancialAccountRepository accounts,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var itens = await accounts.ListAsync(includeInactive, cancellationToken);

        return TypedResults.Ok(itens.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateAccountAsync(
        [FromBody] SaveFinancialAccountRequest request,
        IFinancialAccountRepository accounts,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return TenantRequired();
        }

        var tipo = Enum.TryParse<FinancialAccountKind>(request.Kind, ignoreCase: true, out var lido)
            ? lido
            : FinancialAccountKind.Caixa;

        FinancialAccount conta;
        try
        {
            conta = FinancialAccount.Register(tenantId, request.Name, tipo, timeProvider.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        accounts.Add(conta);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == "uq_financial_accounts_tenant_nome")
        {
            // A constraint é quem decide, não um SELECT antes do INSERT: duas
            // requisições simultâneas com o mesmo nome passariam as duas pela
            // verificação prévia.
            return TypedResults.Problem(
                title: "Nome já usado",
                detail: $"Já existe uma conta chamada \"{request.Name}\" nesta igreja.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/api/v1/giving/accounts/{conta.PublicId}", ToResponse(conta));
    }
}
