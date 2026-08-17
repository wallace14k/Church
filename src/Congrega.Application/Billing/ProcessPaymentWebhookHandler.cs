using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Billing;

/// <summary>O que a borda entrega ao handler: corpo cru, cabeçalho e instante.</summary>
public sealed record PaymentWebhookRequest
{
    /// <summary>Corpo exatamente como chegou. Reserializar mudaria o HMAC.</summary>
    public required string Payload { get; init; }

    public required string? SignatureHeader { get; init; }
    public required WebhookProvider Provider { get; init; }
    public string? CorrelationId { get; init; }
}

public enum WebhookOutcome
{
    /// <summary>Processado agora.</summary>
    Processed,

    /// <summary>Já tinha sido processado antes. Reentrega — resposta de sucesso.</summary>
    Duplicate,

    /// <summary>Assinatura inválida ou fora da janela de replay.</summary>
    Rejected,

    /// <summary>Autêntico, mas de um tipo que não nos interessa.</summary>
    Ignored,
}

public sealed record WebhookResult(WebhookOutcome Outcome, string? Detail = null);

/// <summary>
/// Recebe um webhook de pagamento.
/// </summary>
/// <remarks>
/// <para>
/// Segue o pipeline da skill de segurança, e a <b>ordem importa</b>:
/// </para>
/// <code>
/// recebe → confere assinatura → confere replay → valida schema
///        → checa idempotência → persiste cru → commit → processa
/// </code>
/// <para>
/// <b>Nunca confia no corpo do evento.</b> Mesmo com assinatura válida, o valor
/// autoritativo do pagamento é buscado de volta no provedor
/// (<i>fetch-on-notify</i>) — um evento antigo, legitimamente assinado, poderia
/// ser reapresentado para reabrir uma cobrança já estornada. A assinatura prova
/// quem mandou, não que a informação ainda vale.
/// </para>
/// <para>
/// <b>"Pagamento aprovado" não é "usuário premium".</b> A confirmação emite
/// <see cref="PaymentConfirmed"/>; quem transforma isso em acesso é
/// <see cref="GrantEntitlementHandler"/>, passando pela tabela de entitlements —
/// o único caminho de autorização de conteúdo.
/// </para>
/// </remarks>
public sealed class ProcessPaymentWebhookHandler(
    IWebhookSignatureVerifier verifier,
    IPaymentWebhookRepository webhooks,
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ProcessPaymentWebhookHandler> logger)
{
    public async Task<WebhookResult> HandleAsync(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var agora = timeProvider.GetUtcNow();

        // 1 e 2 — autenticidade e replay, antes de qualquer leitura do conteúdo.
        bool assinaturaValida = verifier.IsValid(request.Payload, request.SignatureHeader, agora);

        // 3 — schema. Feito mesmo com assinatura inválida, porque o evento é
        // gravado nos dois casos e precisa de um identificador para a chave.
        if (!WebhookEnvelope.TryParse(request.Payload, out var envelope))
        {
            logger.LogWarning(
                "Webhook de {Provider} com corpo ilegível. Assinatura válida: {Valida}.",
                request.Provider, assinaturaValida);

            return new WebhookResult(WebhookOutcome.Rejected, "Corpo ilegível.");
        }

        // 4 e 5 — idempotência e persistência do cru, numa operação só. O
        // registro acontece MESMO com assinatura inválida: descartar na porta
        // apagaria a evidência de uma tentativa de forjar pagamento, que é
        // exatamente o que se quer poder investigar depois.
        bool inedito = await webhooks.TryRecordAsync(
            new ReceivedWebhook
            {
                Provider = request.Provider,
                ProviderEventId = envelope.EventId,
                EventType = envelope.EventType,
                Payload = request.Payload,
                SignatureValid = assinaturaValida,
                CorrelationId = request.CorrelationId,
            },
            cancellationToken);

        if (!inedito)
        {
            // Reentrega. O provedor precisa de 2xx, senão continua reenviando.
            logger.LogInformation(
                "Webhook {EventId} de {Provider} já registrado. Reentrega ignorada.",
                envelope.EventId, request.Provider);

            return new WebhookResult(WebhookOutcome.Duplicate);
        }

        if (!assinaturaValida)
        {
            // Gravado acima para auditoria, mas não processado. A resposta ao
            // remetente não distingue "assinatura errada" de "evento
            // desconhecido" — não vale ensinar quem sonda o endpoint onde
            // exatamente ele errou.
            logger.LogWarning(
                "Webhook {EventId} de {Provider} com assinatura inválida. Registrado, não processado.",
                envelope.EventId, request.Provider);

            return new WebhookResult(WebhookOutcome.Rejected, "Assinatura inválida.");
        }

        if (envelope.ChargeId is not { Length: > 0 } chargeId)
        {
            await webhooks.MarkProcessedAsync(request.Provider, envelope.EventId, agora, cancellationToken);
            return new WebhookResult(WebhookOutcome.Ignored, "Evento sem cobrança associada.");
        }

        try
        {
            var resultado = await ProcessarCobrancaAsync(chargeId, agora, cancellationToken);
            await webhooks.MarkProcessedAsync(request.Provider, envelope.EventId, agora, cancellationToken);
            return resultado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falha de processamento não pode apagar o registro do evento: ele
            // fica pendente, com o erro, para o reprocessamento. Por isso
            // `MarkFailed` e não um rollback do que foi gravado.
            logger.LogError(
                ex,
                "Falha ao processar webhook {EventId} de {Provider}.",
                envelope.EventId, request.Provider);

            await webhooks.MarkFailedAsync(
                request.Provider, envelope.EventId, ex.Message, cancellationToken);

            throw;
        }
    }

    private async Task<WebhookResult> ProcessarCobrancaAsync(
        string chargeId,
        DateTimeOffset agora,
        CancellationToken cancellationToken)
    {
        // FETCH-ON-NOTIFY. O corpo do webhook diz apenas "olhe esta cobrança";
        // o estado que vale é o que o provedor responde agora.
        var snapshot = await gateway.FetchChargeAsync(chargeId, cancellationToken);

        if (snapshot is null)
        {
            logger.LogWarning("Cobrança {ChargeId} não existe no provedor.", chargeId);
            return new WebhookResult(WebhookOutcome.Ignored, "Cobrança desconhecida no provedor.");
        }

        var pagamento = await payments.FindByGatewayChargeIdAsync(chargeId, cancellationToken);

        if (pagamento is null)
        {
            // Cobrança que existe no provedor mas não aqui: pode ser de outro
            // ambiente apontando para a mesma conta de sandbox. Ignorar é mais
            // seguro que inventar um pagamento sem titular.
            logger.LogWarning(
                "Cobrança {ChargeId} existe no provedor mas não há pagamento local.", chargeId);

            return new WebhookResult(WebhookOutcome.Ignored, "Pagamento local não encontrado.");
        }

        bool mudou = snapshot.Status switch
        {
            GatewayChargeStatus.Paid =>
                pagamento.Confirm(snapshot.PaidAt ?? agora, agora),

            GatewayChargeStatus.Failed =>
                pagamento.Fail(snapshot.FailureReason ?? "Recusado pelo provedor.", agora),

            GatewayChargeStatus.Refunded =>
                pagamento.Refund(agora),

            // Pendente e cancelada não movem o pagamento: pendente é o estado em
            // que ele já nasceu, e cancelamento sem cobrança paga não tem o que
            // desfazer.
            _ => false,
        };

        if (mudou)
        {
            // Os eventos de domínio acumulados vão para o Outbox nesta mesma
            // transação — é o que garante que não existe pagamento confirmado
            // cujo evento de concessão de acesso se perdeu.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Pagamento {PaymentId} passou para {Status} pela cobrança {ChargeId}.",
                pagamento.Id, pagamento.Status, chargeId);
        }

        return new WebhookResult(WebhookOutcome.Processed);
    }
}
