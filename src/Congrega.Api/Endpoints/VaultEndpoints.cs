using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Domain.Common;
using Congrega.Domain.Giving;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

/// <summary>Uma linha do extrato do cofre.</summary>
public sealed record VaultMovementResponse
{
    public required Guid Id { get; init; }
    public required long SequenceNumber { get; init; }

    /// <summary><c>Deposito</c> (caixa → cofre) ou <c>Retirada</c> (cofre → caixa).</summary>
    public required string Direction { get; init; }

    /// <summary>Centavos, sempre positivo. O sentido vem de <c>direction</c>.</summary>
    public required long AmountCents { get; init; }

    /// <summary>Saldo do cofre depois deste movimento. Torna o extrato conferível linha a linha.</summary>
    public required long BalanceAfterCents { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
    public string? AccountName { get; init; }
    public string? PerformedByName { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// O cofre: quanto tem e o que aconteceu.
/// </summary>
/// <remarks>
/// <b>Nada aqui entra no fechamento do mês.</b> Mover dinheiro entre o caixa e o
/// cofre é a mesma nota mudando de gaveta: não é receita nem despesa, e somá-lo
/// ao resultado faria guardar dinheiro parecer gastá-lo.
/// </remarks>
public sealed record VaultResponse
{
    public required long BalanceCents { get; init; }
    public required int MovementCount { get; init; }
    public required IReadOnlyList<VaultMovementResponse> Movements { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required bool HasNext { get; init; }
}

public sealed record CreateVaultMovementRequest
{
    /// <summary><c>Deposito</c> ou <c>Retirada</c>.</summary>
    [Required, MaxLength(20)]
    public required string Direction { get; init; }

    [Range(1, long.MaxValue)]
    public required long AmountCents { get; init; }

    /// <summary>De onde saiu, ou para onde voltou. Opcional.</summary>
    public Guid? AccountId { get; init; }

    [MaxLength(VaultMovement.MaxNotesLength)]
    public string? Notes { get; init; }
}

public static class VaultEndpoints
{
    public static void MapVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/giving/vault").WithTags("Financeiro");

        // **Leitura também exige `Giving.Vault`, e não `Giving.Read`.**
        //
        // Saber quanto há no cofre é informação sensível por si só: quem sabe
        // que há R$ 8.000 guardados sabe o que procurar. Não é a mesma coisa que
        // ver o fechamento do mês, e não deve seguir a mesma permissão.
        group.MapGet("/", GetVaultAsync)
            .RequireAuthorization(Policies.GivingVault)
            .WithSummary("Saldo e extrato do cofre");

        group.MapPost("/movements", CreateMovementAsync)
            .RequireAuthorization(Policies.GivingVault)
            .WithSummary("Guarda ou retira dinheiro do cofre");
    }

    private static async Task<IResult> GetVaultAsync(
        IVaultRepository vault,
        ITenantContext tenant,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (tenant.TenantId is null)
        {
            return TenantRequired();
        }

        var pagina = Math.Max(1, page);
        var tamanho = Math.Clamp(pageSize, 1, 100);

        var estado = await vault.GetStateAsync(cancellationToken);
        var extrato = await vault.ListAsync(pagina, tamanho, cancellationToken);

        return TypedResults.Ok(new VaultResponse
        {
            BalanceCents = estado.BalanceCents,
            MovementCount = estado.MovementCount,
            Movements = [.. extrato.Items.Select(ToResponse)],
            Page = extrato.Page,
            PageSize = extrato.PageSize,
            TotalCount = extrato.TotalCount,
            HasNext = extrato.HasNext,
        });
    }

    private static async Task<IResult> CreateMovementAsync(
        [FromBody] CreateVaultMovementRequest request,
        IVaultRepository vault,
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

        if (!Enum.TryParse<VaultDirection>(request.Direction, ignoreCase: true, out var direcao)
            || !Enum.IsDefined(direcao))
        {
            return TypedResults.Problem(
                title: "Direção inválida",
                detail: "Informe \"Deposito\" ou \"Retirada\".",
                statusCode: StatusCodes.Status400BadRequest);
        }

        long? contaId = null;
        if (request.AccountId is { } contaPublica)
        {
            var conta = await accounts.FindByPublicIdAsync(contaPublica, cancellationToken);

            if (conta is null)
            {
                return TypedResults.Problem(
                    title: "Conta não encontrada",
                    detail: "A conta informada não existe ou não pertence à sua igreja.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            contaId = conta.Id;
        }

        var estado = await vault.GetStateAsync(cancellationToken);

        VaultMovement movimento;
        try
        {
            movimento = VaultMovement.Register(
                tenantId: tenantId,
                direction: direcao,
                amountCents: request.AmountCents,
                saldoAtualCents: estado.BalanceCents,
                ultimaSequencia: estado.LastSequence,
                now: timeProvider.GetUtcNow(),
                accountId: contaId,
                notes: request.Notes,
                performedByUserId: tenant.UserId);
        }
        catch (InvalidOperationException ex)
        {
            // 409 e não 400: o pedido está bem formado, o estado é que não
            // permite. A mensagem já diz quanto o cofre tem.
            return TypedResults.Problem(
                title: "Saldo insuficiente no cofre",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(
                title: "Dados inválidos",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        vault.Add(movimento);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == "uq_vault_movements_sequencia")
        {
            // **Duas operações no cofre ao mesmo tempo.** As duas leram o mesmo
            // saldo e calcularam a mesma sequência; o índice único derrubou
            // esta. É exatamente o caso que uma verificação prévia deixaria
            // passar — as duas retiradas seriam aprovadas contra o mesmo saldo.
            //
            // 409 com instrução de repetir: a operação é legítima, só precisa
            // ser recalculada sobre o saldo que a outra deixou.
            return TypedResults.Problem(
                title: "O cofre foi movimentado ao mesmo tempo",
                detail: "Outra operação no cofre aconteceu neste instante. Confira o saldo e tente de novo.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created(
            $"/api/v1/giving/vault",
            new VaultMovementResponse
            {
                Id = movimento.PublicId,
                SequenceNumber = movimento.SequenceNumber,
                Direction = movimento.Direction.ToString(),
                AmountCents = movimento.AmountCents,
                BalanceAfterCents = movimento.BalanceAfterCents,
                OccurredAt = movimento.OccurredAt,
                Notes = movimento.Notes,
            });
    }

    private static VaultMovementResponse ToResponse(VaultMovementListItem item) =>
        new()
        {
            Id = item.PublicId,
            SequenceNumber = item.SequenceNumber,
            Direction = item.Direction.ToString(),
            AmountCents = item.AmountCents,
            BalanceAfterCents = item.BalanceAfterCents,
            OccurredAt = item.OccurredAt,
            AccountName = item.AccountName,
            PerformedByName = item.PerformedByName,
            Notes = item.Notes,
        };

    private static ProblemHttpResult TenantRequired() =>
        TypedResults.Problem(
            title: "Igreja não informada",
            detail: "Selecione a igreja antes de acessar o cofre.",
            statusCode: StatusCodes.Status400BadRequest);
}
