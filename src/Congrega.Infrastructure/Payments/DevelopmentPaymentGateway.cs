using System.Collections.Concurrent;
using System.Globalization;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Congrega.Infrastructure.Payments;

/// <summary>
/// Gateway falso, só para desenvolvimento.
/// </summary>
/// <remarks>
/// <para>
/// Existe pela mesma razão que <c>DevelopmentEmailSender</c>: permite exercer o
/// fluxo de checkout e webhook ponta a ponta sem contratar provedor nenhum.
/// <b>Em produção não há adaptador registrado</b>, e a resolução falha no
/// startup — comportamento correto, porque subir cobrando de verdade contra um
/// gateway falso seria muito pior do que não subir. Ver a premissa P8.
/// </para>
/// <para>
/// O estado das cobranças vive em memória e some no restart. Isso é aceitável
/// aqui e <b>só</b> aqui: o pagamento de verdade está no Postgres; este
/// dicionário guarda apenas o que o "provedor" responderia.
/// </para>
/// </remarks>
internal sealed class DevelopmentPaymentGateway(
    TimeProvider timeProvider,
    ILogger<DevelopmentPaymentGateway> logger) : IPaymentGateway
{
    private static readonly ConcurrentDictionary<string, ChargeSnapshot> Cobrancas = new(StringComparer.Ordinal);

    public Task<ChargeResult> CreateChargeAsync(
        ChargeRequest request,
        CancellationToken cancellationToken)
    {
        // Identificador derivado da chave de idempotência: reenviar o mesmo
        // checkout devolve a MESMA cobrança, como um provedor de verdade faria.
        // Gerar um id aleatório aqui esconderia justamente o bug que a chave de
        // idempotência existe para pegar.
        string chargeId = string.Create(
            CultureInfo.InvariantCulture, $"dev_ch_{request.IdempotencyKey}");

        var snapshot = Cobrancas.GetOrAdd(chargeId, id => new ChargeSnapshot
        {
            ChargeId = id,
            Status = GatewayChargeStatus.Pending,
            AmountCents = request.AmountCents,
        });

        logger.LogWarning(
            "GATEWAY DE DESENVOLVIMENTO: cobrança {ChargeId} de {Valor} centavos criada. "
            + "Nenhum dinheiro foi movimentado.",
            chargeId, request.AmountCents);

        return Task.FromResult(new ChargeResult
        {
            ChargeId = chargeId,
            Status = snapshot.Status,
            CheckoutUrl = string.Create(CultureInfo.InvariantCulture, $"https://exemplo.invalido/checkout/{chargeId}"),
            PixCode = string.Create(CultureInfo.InvariantCulture, $"00020126-DEV-{request.IdempotencyKey}"),
        });
    }

    public Task<ChargeSnapshot?> FetchChargeAsync(string chargeId, CancellationToken cancellationToken) =>
        Task.FromResult(Cobrancas.TryGetValue(chargeId, out var snapshot) ? snapshot : null);

    /// <summary>
    /// Move a cobrança de estado, simulando o que aconteceria no provedor.
    /// </summary>
    /// <remarks>
    /// Usado pelo endpoint de simulação, disponível <b>apenas</b> em
    /// desenvolvimento. É o que permite exercer o <i>fetch-on-notify</i> de
    /// verdade: o webhook chega, o handler pergunta ao gateway, e o gateway
    /// responde o estado que foi simulado aqui.
    /// </remarks>
    public bool Simular(string chargeId, GatewayChargeStatus status)
    {
        if (!Cobrancas.TryGetValue(chargeId, out var atual))
        {
            return false;
        }

        Cobrancas[chargeId] = atual with
        {
            Status = status,
            PaidAt = status == GatewayChargeStatus.Paid ? timeProvider.GetUtcNow() : atual.PaidAt,
            FailureReason = status == GatewayChargeStatus.Failed ? "Recusa simulada." : null,
        };

        return true;
    }
}
