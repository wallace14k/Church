using System.ComponentModel.DataAnnotations;
using Congrega.Api.Authorization;
using Congrega.Application.Abstractions;
using Congrega.Application.Billing;
using Congrega.Domain.Billing;
using Microsoft.AspNetCore.Mvc;

namespace Congrega.Api.Endpoints;

public sealed record StartCheckoutRequest
{
    /// <summary>
    /// Código do plano. É o único dado ligado a preço que o cliente envia — o
    /// valor vem de <c>plans</c>, no servidor.
    /// </summary>
    [Required, MaxLength(50)]
    public required string PlanCode { get; init; }
}

public sealed record CheckoutResponse
{
    public required Guid PaymentId { get; init; }
    public required long AmountCents { get; init; }
    public required string Status { get; init; }
    public string? PlanName { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? PixCode { get; init; }

    /// <summary>
    /// <c>true</c> quando a chave de idempotência já tinha sido usada e esta é a
    /// cobrança original, não uma nova.
    /// </summary>
    public required bool Reused { get; init; }
}

/// <summary>Estado da assinatura Congrega+ do titular — o que a aba de assinatura mostra.</summary>
public sealed record SubscriptionStatusResponse
{
    public required bool HasSubscription { get; init; }
    public string? PlanCode { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Espelha <see cref="SubscriptionStatus"/> — ver a máquina de estados em docs/03-arquitetura.md §6.</summary>
    public string? Status { get; init; }
    public DateTimeOffset? CurrentPeriodEnd { get; init; }
    public DateTimeOffset? GraceUntil { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
}

/// <summary>Um item do catálogo — o que a tela de escolha de plano lista.</summary>
public sealed record PlanSummaryResponse
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required long PriceCents { get; init; }

    /// <summary>1=Mensal 2=Anual, conforme <c>plans.billing_period</c>.</summary>
    public required short BillingPeriod { get; init; }
}

/// <summary>Uma cobrança do histórico do titular.</summary>
/// <remarks>
/// <c>PublicId</c>, nunca a PK sequencial: o identificador aparece em payload,
/// e PK exposta é enumeração — a discordância D1 do <c>docs/00-premissas.md</c>.
/// </remarks>
public sealed record PaymentSummaryResponse
{
    public required Guid Id { get; init; }
    public required long AmountCents { get; init; }
    public required string Status { get; init; }
    public string? Method { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
}

/// <summary>
/// Cobrança: abrir checkout e receber webhook do gateway.
/// </summary>
/// <remarks>
/// As duas rotas têm posturas opostas de propósito. O checkout exige identidade
/// verificada e resolve o titular pela claim. O webhook é <b>anônimo</b> — o
/// gateway não tem como apresentar um JWT nosso —, e sua autenticação é o HMAC
/// do corpo, conferido antes de qualquer decisão.
/// </remarks>
public static class BillingEndpoints
{
    /// <summary>
    /// Cabeçalho de idempotência. Obrigatório: sem ele, um duplo clique ou um
    /// retry de rede vira a segunda cobrança do mesmo usuário.
    /// </summary>
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>
    /// Teto do corpo do webhook.
    /// </summary>
    /// <remarks>
    /// A rota é anônima e lê o corpo inteiro em memória para conferir o HMAC —
    /// o cálculo precisa dos bytes exatos, então não há como processar em fluxo.
    /// Sem teto, qualquer um na internet transformaria isso em consumo de
    /// memória do processo enviando um corpo enorme.
    /// </remarks>
    private const int MaxWebhookBodyBytes = 64 * 1024;

    /// <summary>
    /// Teto do histórico de pagamentos devolvido numa chamada.
    /// </summary>
    /// <remarks>
    /// Lista com teto em vez de paginação completa, de propósito: o histórico de
    /// um assinante é uma linha por ciclo de cobrança — cinquenta cobre mais de
    /// quatro anos de plano mensal. Montar `PagedResponse` aqui seria cerimônia
    /// sobre um conjunto que não pagina. O teto continua obrigatório: sem ele a
    /// resposta cresce sem limite com o tempo de casa do cliente.
    /// </remarks>
    private const int MaxPaymentHistory = 50;

    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/billing").WithTags("Cobrança");

        group.MapPost("/checkout", StartCheckoutAsync)
            .RequireAuthorization(Policies.BillingCheckout)
            .WithSummary("Abre uma cobrança de assinatura Congrega+");

        group.MapGet("/subscription", GetSubscriptionAsync)
            .RequireAuthorization(Policies.BillingCheckout)
            .WithSummary("Assinatura Congrega+ ativa do titular, se houver");

        group.MapGet("/plans", ListPlansAsync)
            .RequireAuthorization(Policies.BillingCheckout)
            .WithSummary("Catálogo de planos Congrega+ disponíveis para assinar");

        group.MapGet("/payments", ListPaymentsAsync)
            .RequireAuthorization(Policies.BillingCheckout)
            .WithSummary("Histórico de cobranças do titular");

        group.MapPost("/subscription/cancel", CancelSubscriptionAsync)
            .RequireAuthorization(Policies.BillingCheckout)
            .WithSummary("Cancela a renovação da assinatura Congrega+ do titular");

        group.MapPost("/webhook", ReceiveWebhookAsync)
            .AllowAnonymous()
            .WithSummary("Recebe notificação de cobrança do gateway");
    }

    private static async Task<IResult> GetSubscriptionAsync(
        HttpContext http,
        ISubscriptionStore subscriptions,
        IPlanRepository plans,
        CancellationToken cancellationToken)
    {
        long? userId = http.User.GetUserId();

        if (userId is not { } titular)
        {
            return TypedResults.Problem(
                title: "Sessão inválida",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var assinatura = await subscriptions.FindCurrentByUserAsync(titular, cancellationToken);

        if (assinatura is null)
        {
            // Estado normal de quem nunca assinou — não é 404, que sinalizaria
            // erro. A tela decide o que mostrar (paywall) a partir da flag.
            return TypedResults.Ok(new SubscriptionStatusResponse { HasSubscription = false });
        }

        // O plano pode, em tese, ter sido desativado depois da assinatura —
        // por isso FindByIdAsync (busca por id, sem o filtro is_active de
        // FindByCodeAsync não faria diferença aqui) pode devolver null; nesse
        // caso o nome fica ausente, mas o status da assinatura continua válido.
        var plano = await plans.FindByIdAsync(assinatura.PlanId, cancellationToken);

        return TypedResults.Ok(new SubscriptionStatusResponse
        {
            HasSubscription = true,
            PlanCode = plano?.Code,
            PlanName = plano?.Name,
            Status = assinatura.Status.ToString(),
            CurrentPeriodEnd = assinatura.CurrentPeriodEnd,
            GraceUntil = assinatura.GraceUntil,
            CancelAtPeriodEnd = assinatura.CancelAtPeriodEnd,
        });
    }

    /// <summary>
    /// Histórico de cobranças do titular.
    /// </summary>
    /// <remarks>
    /// O titular sai da claim <c>sub</c>, nunca de parâmetro — não existe
    /// <c>?userId=</c> aqui de propósito. Aceitar o titular do cliente seria
    /// entregar o histórico financeiro de qualquer pessoa a quem trocasse o
    /// número na URL; é o IDOR/BOLA da §5 da skill de segurança, e a defesa é
    /// não oferecer o parâmetro, não validá-lo depois.
    /// </remarks>
    private static async Task<IResult> ListPaymentsAsync(
        HttpContext http,
        IPaymentRepository payments,
        CancellationToken cancellationToken)
    {
        long? userId = http.User.GetUserId();

        if (userId is not { } titular)
        {
            return TypedResults.Problem(
                title: "Sessão inválida",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var historico = await payments.ListByUserAsync(titular, MaxPaymentHistory, cancellationToken);

        return TypedResults.Ok(historico.Select(p => new PaymentSummaryResponse
        {
            Id = p.PublicId,
            AmountCents = p.AmountCents,
            Status = p.Status.ToString(),
            Method = p.Method?.ToString(),
            CreatedAt = p.CreatedAt,
            PaidAt = p.PaidAt,
        }).ToList());
    }

    /// <summary>
    /// Cancela a renovação da assinatura do titular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cancelar não revoga acesso.</b> <c>Subscription.Cancel</c> é chamado
    /// sem <c>immediate</c>, então <c>CurrentPeriodEnd</c> não se move e os
    /// entitlements concedidos seguem válidos até lá — a pessoa pagou por
    /// aquele período. Confundir "cancelou" com "perdeu acesso agora" é o que
    /// gera reclamação e chargeback; está no agregado e na §6 do
    /// <c>docs/03-arquitetura.md</c>.
    /// </para>
    /// <para>
    /// <b>A assinatura vem do titular autenticado, não de um id no corpo.</b>
    /// Não há como pedir o cancelamento da assinatura alheia porque não há onde
    /// informá-la.
    /// </para>
    /// <para>
    /// <b>Transição recusada é 409, não 500.</b> <c>FindCurrentByUserAsync</c>
    /// devolve <c>Active</c>, <c>PastDue</c> <b>e</b> <c>Grace</c>, mas a tabela
    /// de transições do agregado só admite cancelamento a partir dos dois
    /// primeiros — <c>Grace</c> já está a caminho de <c>Expired</c> por conta da
    /// cobrança que falhou, e não há renovação futura para cancelar. Sem este
    /// <c>catch</c>, esse caminho — que a tela pode alcançar — sobe como erro
    /// não tratado.
    /// </para>
    /// </remarks>
    private static async Task<IResult> CancelSubscriptionAsync(
        HttpContext http,
        ISubscriptionStore subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        long? userId = http.User.GetUserId();

        if (userId is not { } titular)
        {
            return TypedResults.Problem(
                title: "Sessão inválida",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var assinatura = await subscriptions.FindCurrentByUserAsync(titular, cancellationToken);

        if (assinatura is null)
        {
            return TypedResults.Problem(
                title: "Nenhuma assinatura ativa",
                detail: "Não há assinatura Congrega+ em vigor para cancelar.",
                statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
            assinatura.Cancel(timeProvider.GetUtcNow());
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            return TypedResults.Problem(
                title: "Não é possível cancelar agora",
                detail: ex.From == SubscriptionStatus.Grace
                    ? "Esta assinatura já está encerrando por falta de pagamento e não renova."
                    : "A assinatura está em um estado que não admite cancelamento.",
                statusCode: StatusCodes.Status409Conflict);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Mesma forma que GET /subscription devolveria, plano incluído: a tela
        // aplica a resposta direto, sem uma segunda ida ao servidor só para
        // redescobrir o que esta chamada já sabe.
        var plano = await plans.FindByIdAsync(assinatura.PlanId, cancellationToken);

        return TypedResults.Ok(new SubscriptionStatusResponse
        {
            HasSubscription = true,
            PlanCode = plano?.Code,
            PlanName = plano?.Name,
            Status = assinatura.Status.ToString(),
            CurrentPeriodEnd = assinatura.CurrentPeriodEnd,
            GraceUntil = assinatura.GraceUntil,
            CancelAtPeriodEnd = assinatura.CancelAtPeriodEnd,
        });
    }

    private static async Task<IResult> ListPlansAsync(
        IPlanRepository plans,
        CancellationToken cancellationToken)
    {
        var catalogo = await plans.ListActiveAsync(PlanAudience.User, cancellationToken);

        return TypedResults.Ok(catalogo.Select(p => new PlanSummaryResponse
        {
            Code = p.Code,
            Name = p.Name,
            PriceCents = p.PriceCents,
            BillingPeriod = p.BillingPeriod,
        }).ToList());
    }

    private static async Task<IResult> StartCheckoutAsync(
        [FromBody] StartCheckoutRequest request,
        HttpContext http,
        StartCheckoutHandler handler,
        CancellationToken cancellationToken)
    {
        long? userId = http.User.GetUserId();

        if (userId is not { } titular)
        {
            // A policy já exige autenticação; chegar aqui sem `sub` legível
            // significa token malformado, não falta de permissão.
            return TypedResults.Problem(
                title: "Sessão inválida",
                detail: "Não foi possível identificar o titular da cobrança.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!http.Request.Headers.TryGetValue(IdempotencyHeader, out var valores) ||
            valores.ToString() is not { Length: > 0 } chave)
        {
            // Recusar é mais seguro do que gerar uma chave no servidor: uma chave
            // gerada aqui seria diferente a cada requisição, e o retry do cliente
            // — exatamente o caso que a idempotência existe para cobrir — criaria
            // a segunda cobrança sem que nada acusasse.
            return TypedResults.Problem(
                title: "Cabeçalho obrigatório ausente",
                detail: $"Informe {IdempotencyHeader} com um valor estável para esta tentativa de compra.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (chave.Length > StartCheckoutHandler.MaxIdempotencyKeyLength)
        {
            return TypedResults.Problem(
                title: "Chave de idempotência longa demais",
                detail: $"O limite é de {StartCheckoutHandler.MaxIdempotencyKeyLength} caracteres.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var resultado = await handler.HandleAsync(
            new StartCheckoutCommand
            {
                UserId = titular,
                PlanCode = request.PlanCode,
                IdempotencyKey = chave,
            },
            cancellationToken);

        if (resultado.Outcome == CheckoutOutcome.PlanUnavailable)
        {
            return TypedResults.Problem(
                title: "Plano indisponível",
                detail: resultado.Detail,
                statusCode: StatusCodes.Status404NotFound);
        }

        if (resultado.Outcome == CheckoutOutcome.SubscriptionConflict)
        {
            return TypedResults.Problem(
                title: "Assinatura em andamento",
                detail: resultado.Detail,
                statusCode: StatusCodes.Status409Conflict);
        }

        var corpo = new CheckoutResponse
        {
            PaymentId = resultado.PaymentId,
            AmountCents = resultado.AmountCents,
            Status = resultado.Status,
            PlanName = resultado.PlanName,
            CheckoutUrl = resultado.CheckoutUrl,
            PixCode = resultado.PixCode,
            Reused = resultado.Outcome == CheckoutOutcome.Reused,
        };

        // 200 na reutilização, 201 na criação: o cliente que recebe 200 sabe que
        // não abriu nada novo, e o 201 carrega o Location da cobrança.
        return resultado.Outcome == CheckoutOutcome.Reused
            ? TypedResults.Ok(corpo)
            : TypedResults.Created($"/api/v1/billing/payments/{resultado.PaymentId}", corpo);
    }

    private static async Task<IResult> ReceiveWebhookAsync(
        HttpContext http,
        ReceivePaymentWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        // Corpo CRU, lido byte a byte. Deixar o binder desserializar e depois
        // reserializar mudaria espaços e ordem de chaves, e o HMAC — que cobre os
        // bytes exatos — deixaria de conferir para todo evento legítimo.
        http.Request.EnableBuffering();

        string payload;
        using (var leitor = new StreamReader(http.Request.Body, leaveOpen: true))
        {
            var buffer = new char[MaxWebhookBodyBytes + 1];
            int lidos = await leitor.ReadBlockAsync(buffer, cancellationToken);

            if (lidos > MaxWebhookBodyBytes)
            {
                return TypedResults.Problem(
                    title: "Corpo grande demais",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            payload = new string(buffer, 0, lidos);
        }

        var resultado = await handler.HandleAsync(
            new PaymentWebhookRequest
            {
                Payload = payload,
                SignatureHeader = http.Request.Headers["X-Congrega-Signature"].ToString(),
                Provider = WebhookProvider.AbacatePay,
                CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString(),
            },
            cancellationToken);

        return resultado.Outcome switch
        {
            // Aceito e enfileirado. 202, não 200: o processamento ainda não
            // aconteceu, e prometer o contrário ao provedor seria mentira.
            WebhookOutcome.Accepted => TypedResults.Accepted((string?)null),

            // Reentrega. Precisa de 2xx, senão o provedor continua reenviando
            // um evento que já está registrado.
            WebhookOutcome.Duplicate => TypedResults.Ok(),

            // Assinatura inválida e corpo ilegível compartilham a MESMA resposta,
            // sem detalhe: dizer qual dos dois falhou ensina quem está sondando o
            // endpoint exatamente onde errou.
            _ => TypedResults.BadRequest(),
        };
    }
}
