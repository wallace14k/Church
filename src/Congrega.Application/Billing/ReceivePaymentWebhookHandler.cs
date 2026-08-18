using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Billing;

/// <summary>
/// A borda do webhook: confere, registra e devolve. <b>Não processa.</b>
/// </summary>
/// <remarks>
/// <para>
/// Faz os passos 1 a 5 do pipeline — assinatura, replay, schema, idempotência,
/// persistência do cru — e para aí. O processamento (fetch-on-notify e máquina
/// de estados do pagamento) fica com
/// <see cref="ProcessPaymentWebhookHandler"/>, rodando no worker.
/// </para>
/// <para>
/// <b>A divisão não é estilística, é uma restrição do RLS.</b> A requisição do
/// gateway é anônima por natureza: não há JWT, logo não há
/// <c>app.user_id</c> nem <c>app.tenant_id</c> na conexão. A API roda com
/// <c>congrega_app</c>, que tem RLS aplicado, e a policy de <c>payments</c>
/// filtra por <c>tenant_id</c> ou <c>user_id</c> — nenhum dos dois definido.
/// Tocar em <c>payments</c> daqui não daria erro barulhento: daria
/// <b>zero linhas</b>, e o webhook concluiria "pagamento local não encontrado"
/// para toda cobrança legítima. <c>payment_webhooks</c> não tem RLS, de
/// propósito, e é justamente por isso que registrar funciona e processar não.
/// </para>
/// <para>
/// Responder rápido também é o comportamento correto de webhook: o provedor tem
/// timeout curto e reenvia o que demora. Registrar e devolver 202 desacopla o
/// nosso tempo de processamento da política de retry dele — e, como o registro
/// é idempotente pela constraint, o reenvio é inofensivo.
/// </para>
/// </remarks>
public sealed class ReceivePaymentWebhookHandler(
    IWebhookSignatureVerifier verifier,
    IPaymentWebhookRepository webhooks,
    TimeProvider timeProvider,
    ILogger<ReceivePaymentWebhookHandler> logger)
{
    public async Task<WebhookResult> HandleAsync(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var agora = timeProvider.GetUtcNow();

        // 1 e 2 — autenticidade e replay, antes de qualquer leitura do conteúdo.
        bool assinaturaValida = verifier.IsValid(request.Payload, request.SignatureHeader, agora);

        // 3 — schema. Feito mesmo com assinatura inválida: o evento é gravado nos
        // dois casos e precisa de um identificador para compor a chave.
        if (!WebhookEnvelope.TryParse(request.Payload, out var envelope))
        {
            logger.LogWarning(
                "Webhook de {Provider} com corpo ilegível. Assinatura válida: {Valida}.",
                request.Provider, assinaturaValida);

            return new WebhookResult(WebhookOutcome.Rejected, "Corpo ilegível.");
        }

        // 4 e 5 — idempotência e persistência do cru, numa operação só. O registro
        // acontece MESMO com assinatura inválida: descartar na porta apagaria a
        // evidência de uma tentativa de forjar pagamento, que é exatamente o que
        // se quer poder investigar depois.
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
            logger.LogInformation(
                "Webhook {EventId} de {Provider} já registrado. Reentrega ignorada.",
                envelope.EventId, request.Provider);

            return new WebhookResult(WebhookOutcome.Duplicate);
        }

        if (!assinaturaValida)
        {
            logger.LogWarning(
                "Webhook {EventId} de {Provider} com assinatura inválida. Registrado, não processado.",
                envelope.EventId, request.Provider);

            return new WebhookResult(WebhookOutcome.Rejected, "Assinatura inválida.");
        }

        logger.LogInformation(
            "Webhook {EventId} ({EventType}) de {Provider} registrado para processamento.",
            envelope.EventId, envelope.EventType, request.Provider);

        return new WebhookResult(WebhookOutcome.Accepted);
    }
}
