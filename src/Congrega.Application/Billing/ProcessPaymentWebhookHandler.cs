using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Billing;

/// <summary>O que a borda entrega ao handler de recepção: corpo cru, cabeçalho e instante.</summary>
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

    /// <summary>
    /// Registrado na borda e enfileirado — o processamento é do worker.
    /// </summary>
    /// <remarks>
    /// Distinto de <see cref="Processed"/> de propósito: a borda não sabe, e
    /// não pode saber, se o pagamento vai mudar de estado. Reaproveitar
    /// <c>Processed</c> aqui faria o log dizer que algo foi resolvido quando só
    /// foi recebido.
    /// </remarks>
    Accepted,

    /// <summary>O worker tentou processar e não conseguiu — fica para a próxima reivindicação.</summary>
    Failed,
}

public sealed record WebhookResult(WebhookOutcome Outcome, string? Detail = null);

/// <summary>
/// Resolve um webhook de pagamento já registrado contra o estado real da cobrança.
/// </summary>
/// <remarks>
/// <para>
/// Roda no worker, depois que <see cref="ReceivePaymentWebhookHandler"/> já fez
/// os passos 1 a 5 do pipeline da skill de segurança (assinatura, replay,
/// schema, idempotência, persistência do cru) na borda. Este handler só existe
/// para o passo 6: <b>nunca confia no corpo do evento</b>. Mesmo com assinatura
/// válida, o valor autoritativo do pagamento é buscado de volta no provedor
/// (<i>fetch-on-notify</i>) — um evento antigo, legitimamente assinado, poderia
/// ser reapresentado para reabrir uma cobrança já estornada. A assinatura prova
/// quem mandou, não que a informação ainda vale.
/// </para>
/// <para>
/// <b>Não reverifica assinatura nem persiste de novo.</b> Repetir
/// <c>TryRecordAsync</c> sobre uma linha que já existe bateria no <c>ON
/// CONFLICT DO NOTHING</c> e nunca chegaria ao fetch-on-notify — só quem
/// reivindica o lote (<c>IPaymentWebhookRepository.ClaimBatchAsync</c>) já
/// filtra por <c>signature_valid = true</c>, então chegar aqui já significa
/// "autêntico e ainda não processado".
/// </para>
/// <para>
/// <b>"Pagamento aprovado" não é "usuário premium".</b> A confirmação emite
/// <see cref="PaymentConfirmed"/>; quem transforma isso em acesso é
/// <see cref="GrantEntitlementHandler"/>, passando pela tabela de entitlements —
/// o único caminho de autorização de conteúdo. Este handler não conhece
/// entitlements.
/// </para>
/// </remarks>
public sealed class ProcessPaymentWebhookHandler(
    IPaymentWebhookRepository webhooks,
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ProcessPaymentWebhookHandler> logger)
{
    /// <summary>
    /// Processa um evento já reivindicado. Nunca propaga exceção: uma cobrança
    /// com erro é isolada aqui, marcada como falha, e não deve interromper o
    /// resto do lote que o dispatcher está drenando — mesmo espírito do
    /// isolamento de "mensagem venenosa" do <c>OutboxProcessor</c>.
    /// </summary>
    public async Task<WebhookResult> HandleAsync(
        PendingPaymentWebhook evento,
        CancellationToken cancellationToken)
    {
        var agora = timeProvider.GetUtcNow();

        if (!WebhookEnvelope.TryParse(evento.Payload, out var envelope))
        {
            // Não deveria acontecer: o mesmo payload já foi parseado com
            // sucesso para chegar a esta tabela. Se acontecer mesmo assim
            // (corrupção, mudança de formato), não há tentativa futura que
            // resolva — mas ainda passa pelo contador de tentativas em vez de
            // travar o lote.
            logger.LogError(
                "Webhook {EventId} de {Provider} com payload ilegível ao reprocessar.",
                evento.ProviderEventId, evento.Provider);

            await webhooks.MarkFailedAsync(
                evento.Provider, evento.ProviderEventId, "Payload ilegível ao reprocessar.", cancellationToken);

            return new WebhookResult(WebhookOutcome.Failed, "Payload ilegível.");
        }

        if (envelope.ChargeId is not { Length: > 0 } chargeId)
        {
            await webhooks.MarkProcessedAsync(evento.Provider, envelope.EventId, agora, cancellationToken);
            return new WebhookResult(WebhookOutcome.Ignored, "Evento sem cobrança associada.");
        }

        try
        {
            var resultado = await ProcessarCobrancaAsync(chargeId, agora, cancellationToken);
            await webhooks.MarkProcessedAsync(evento.Provider, envelope.EventId, agora, cancellationToken);
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
                envelope.EventId, evento.Provider);

            await webhooks.MarkFailedAsync(
                evento.Provider, envelope.EventId, ex.Message, cancellationToken);

            return new WebhookResult(WebhookOutcome.Failed, ex.Message);
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
