using Congrega.Application.Abstractions;
using Congrega.Domain.Billing;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Billing;

/// <summary>
/// Transforma pagamento confirmado em direito de acesso — e estorno em
/// revogação.
/// </summary>
/// <remarks>
/// <para>
/// Existe como passo separado de propósito. A skill de segurança é explícita:
/// "não trate 'pagamento aprovado' como sinônimo universal de 'usuário
/// premium'; a concessão de acesso deve passar pelo modelo de entitlement".
/// Confirmar a cobrança e liberar o conteúdo no mesmo lugar acabaria, mais
/// cedo ou mais tarde, com alguém checando `payment.Status == Paid` para
/// decidir acesso — e aí um estorno deixaria de cortar o acesso.
/// </para>
/// <para>
/// <b>Idempotente nas duas direções.</b> Conceder duas vezes o mesmo direito
/// estende o prazo em vez de criar uma segunda linha; revogar duas vezes não faz
/// nada na segunda. É o que permite reprocessar a fila do Outbox sem medo.
/// </para>
/// </remarks>
public sealed class GrantEntitlementHandler(
    IEntitlementRepository entitlements,
    ISubscriptionStore subscriptions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<GrantEntitlementHandler> logger)
{
    /// <summary>Concede o acesso correspondente a um pagamento confirmado.</summary>
    public async Task GrantAsync(PaymentConfirmed evento, CancellationToken cancellationToken)
    {
        if (evento.UserId is not { } userId)
        {
            // Pagamento de igreja (B2B) não concede entitlement de conteúdo: o
            // que a igreja compra é o ChMS, cujo acesso vem da membership, não
            // desta tabela. Ver a separação das duas receitas em CLAUDE.md.
            logger.LogInformation(
                "Pagamento {PaymentId} sem usuário titular — nada a conceder.", evento.PaymentId);
            return;
        }

        if (evento.SubscriptionId is not { } subscriptionId)
        {
            logger.LogInformation(
                "Pagamento {PaymentId} sem assinatura — compra avulsa exige o pacote, ainda não implementado.",
                evento.PaymentId);
            return;
        }

        var assinatura = await subscriptions.FindByIdAsync(subscriptionId, cancellationToken);

        if (assinatura is null)
        {
            logger.LogWarning(
                "Assinatura {SubscriptionId} do pagamento {PaymentId} não encontrada.",
                subscriptionId, evento.PaymentId);
            return;
        }

        var agora = timeProvider.GetUtcNow();

        // O direito vale até o fim do período pago. Amarrar as duas datas evita
        // o acesso que sobrevive à assinatura — e o inverso, a assinatura ativa
        // sem acesso.
        var validade = assinatura.CurrentPeriodEnd;

        var existente = await entitlements.FindActiveForPlanAsync(
            userId, assinatura.PlanId, agora, cancellationToken);

        if (existente is not null)
        {
            // Renovação: estende, não duplica. `ExtendTo` nunca encurta, então
            // um webhook fora de ordem não tira dias já pagos.
            existente.ExtendTo(validade);

            logger.LogInformation(
                "Direito do usuário {UserId} no plano {PlanId} estendido até {Validade}.",
                userId, assinatura.PlanId, validade);
        }
        else
        {
            entitlements.Add(Entitlement.GrantForPlan(
                userId: userId,
                planId: assinatura.PlanId,
                source: EntitlementSource.Subscription,
                now: agora,
                expiresAt: validade,
                subscriptionId: subscriptionId,
                paymentId: evento.PaymentId));

            logger.LogInformation(
                "Direito concedido ao usuário {UserId} no plano {PlanId} até {Validade}.",
                userId, assinatura.PlanId, validade);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Revoga o acesso concedido por um pagamento estornado.</summary>
    public async Task RevokeAsync(PaymentRefunded evento, CancellationToken cancellationToken)
    {
        var agora = timeProvider.GetUtcNow();

        var afetados = await entitlements.ListByPaymentAsync(evento.PaymentId, cancellationToken);

        int revogados = 0;
        foreach (var direito in afetados)
        {
            if (direito.Revoke(RevocationReason.Refund, agora))
            {
                revogados++;
            }
        }

        if (revogados > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Estorno do pagamento {PaymentId}: {Revogados} direito(s) revogado(s).",
            evento.PaymentId, revogados);
    }
}
