using Congrega.Application.Outbox;
using Congrega.Domain.Billing;

namespace Congrega.Application.Billing;

/// <summary>
/// Liga <see cref="PaymentConfirmed"/> a <see cref="GrantEntitlementHandler.GrantAsync"/>.
/// </summary>
/// <remarks>
/// Adaptador fino de propósito: a regra de negócio inteira mora em
/// <see cref="GrantEntitlementHandler"/>, testável sem o Outbox. Isto aqui só
/// traduz "mensagem chegou" em "chamada de método" — sem isso, o evento cai em
/// "nenhum handler registrado" no primeiro ciclo do dispatcher e vai para dead
/// letter, e pagamento confirmado nunca vira acesso.
/// </remarks>
public sealed class PaymentConfirmedOutboxHandler(GrantEntitlementHandler entitlements) : IOutboxMessageHandler
{
    public string MessageType => nameof(PaymentConfirmed);

    public Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var evento = OutboxSerialization.Deserialize<PaymentConfirmed>(payloadJson);
        return entitlements.GrantAsync(evento, cancellationToken);
    }
}

/// <summary>Liga <see cref="PaymentRefunded"/> a <see cref="GrantEntitlementHandler.RevokeAsync"/>.</summary>
public sealed class PaymentRefundedOutboxHandler(GrantEntitlementHandler entitlements) : IOutboxMessageHandler
{
    public string MessageType => nameof(PaymentRefunded);

    public Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var evento = OutboxSerialization.Deserialize<PaymentRefunded>(payloadJson);
        return entitlements.RevokeAsync(evento, cancellationToken);
    }
}
