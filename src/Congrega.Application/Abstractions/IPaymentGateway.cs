namespace Congrega.Application.Abstractions;

/// <summary>O que se pede ao gateway para abrir uma cobrança.</summary>
public sealed record ChargeRequest
{
    public required long AmountCents { get; init; }

    /// <summary>
    /// Repassada ao gateway.
    /// </summary>
    /// <remarks>
    /// O provedor precisa dela para que <b>ele também</b> não duplique a
    /// cobrança se a nossa requisição for reenviada por timeout. Idempotência só
    /// do nosso lado não impede a segunda chamada de virar a segunda cobrança
    /// lá dentro.
    /// </remarks>
    public required string IdempotencyKey { get; init; }

    public required string Description { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerName { get; init; }
}

public sealed record ChargeResult
{
    /// <summary>Identificador da cobrança no provedor.</summary>
    public required string ChargeId { get; init; }

    /// <summary>Para onde mandar o pagador — link do Pix, do boleto ou do checkout.</summary>
    public string? CheckoutUrl { get; init; }

    /// <summary>Código copia-e-cola do Pix, quando o método for esse.</summary>
    public string? PixCode { get; init; }

    public required GatewayChargeStatus Status { get; init; }
}

public enum GatewayChargeStatus
{
    Pending,
    Paid,
    Failed,
    Refunded,
    Canceled,
}

/// <summary>Estado de uma cobrança, consultado direto no provedor.</summary>
public sealed record ChargeSnapshot
{
    public required string ChargeId { get; init; }
    public required GatewayChargeStatus Status { get; init; }
    public required long AmountCents { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public string? FailureReason { get; init; }
}

/// <summary>
/// A porta de pagamentos.
/// </summary>
/// <remarks>
/// <para>
/// <b>O domínio nunca depende do SDK do gateway</b> — o fluxo é
/// <c>Application → IPaymentGateway → adaptador</c>. Trocar de provedor precisa
/// ser escrever outro adaptador, não reescrever regra de assinatura.
/// </para>
/// <para>
/// <see cref="FetchChargeAsync"/> existe para o padrão <b>fetch-on-notify</b>: o
/// webhook avisa que <i>algo</i> mudou, e o valor autoritativo é buscado de
/// volta no provedor. Confiar no corpo do webhook — mesmo com assinatura válida
/// — deixaria um replay de um evento antigo, ainda corretamente assinado,
/// reabrir uma cobrança já estornada.
/// </para>
/// </remarks>
public interface IPaymentGateway
{
    Task<ChargeResult> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken);

    /// <summary>Consulta o estado real da cobrança no provedor. Ver fetch-on-notify.</summary>
    Task<ChargeSnapshot?> FetchChargeAsync(string chargeId, CancellationToken cancellationToken);
}

/// <summary>
/// Confere a assinatura de um webhook.
/// </summary>
/// <remarks>
/// Separado de <see cref="IPaymentGateway"/> de propósito: a verificação precisa
/// rodar <b>antes</b> de qualquer decisão sobre o evento, inclusive antes de
/// saber a que cobrança ele se refere. Juntar as duas coisas convidaria a
/// resolver o pagamento primeiro e conferir a assinatura depois — que é a ordem
/// errada.
/// </remarks>
public interface IWebhookSignatureVerifier
{
    /// <summary>
    /// <c>true</c> se o corpo confere com a assinatura e está dentro da janela
    /// de tolerância de tempo.
    /// </summary>
    /// <param name="payload">Corpo cru, exatamente como recebido.</param>
    /// <param name="signatureHeader">Cabeçalho de assinatura enviado pelo provedor.</param>
    /// <param name="receivedAt">Instante da recepção, para a proteção de replay.</param>
    bool IsValid(string payload, string? signatureHeader, DateTimeOffset receivedAt);
}
