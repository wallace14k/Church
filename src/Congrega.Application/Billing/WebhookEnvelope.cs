using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Congrega.Application.Billing;

/// <summary>
/// O mínimo que precisamos ler do corpo do webhook.
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente raso: identificador do evento, tipo e identificador da
/// cobrança. Tudo o mais — valor, status, data de pagamento — é lido do
/// provedor pelo <i>fetch-on-notify</i>, então desserializar o corpo inteiro em
/// um tipo rico só criaria a tentação de confiar nele.
/// </para>
/// </remarks>
public sealed record WebhookEnvelope
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public string? ChargeId { get; init; }

    /// <summary>
    /// Lê o envelope. <c>false</c> para qualquer corpo que não seja JSON com
    /// identificador de evento.
    /// </summary>
    /// <remarks>
    /// Aceita as grafias usuais (<c>id</c>/<c>event_id</c>,
    /// <c>type</c>/<c>event</c>, <c>charge_id</c>/<c>chargeId</c>) porque os
    /// provedores divergem — e trocar de provedor não deve exigir mexer no
    /// handler, só no adaptador e aqui.
    /// </remarks>
    public static bool TryParse(string payload, [NotNullWhen(true)] out WebhookEnvelope? envelope)
    {
        envelope = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var documento = JsonDocument.Parse(payload);
            var raiz = documento.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? eventId = Ler(raiz, "event_id", "eventId", "id");
            if (string.IsNullOrWhiteSpace(eventId))
            {
                // Sem identificador não há como deduplicar, e sem deduplicação
                // um reenvio viraria uma segunda concessão de acesso. Recusar é
                // a única saída segura.
                return false;
            }

            envelope = new WebhookEnvelope
            {
                EventId = eventId,
                EventType = Ler(raiz, "event_type", "eventType", "type", "event") ?? "desconhecido",
                ChargeId = LerAninhado(raiz, "charge_id", "chargeId")
                           ?? LerAninhado(raiz, "data", "charge", "id"),
            };

            return true;
        }
        catch (JsonException)
        {
            // Corpo malformado é entrada não confiável, não erro de programação.
            return false;
        }
    }

    private static string? Ler(JsonElement objeto, params string[] nomes)
    {
        foreach (var nome in nomes)
        {
            if (objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String)
            {
                return valor.GetString();
            }
        }

        return null;
    }

    /// <summary>Lê `a.b.c`, parando em qualquer nível ausente.</summary>
    private static string? LerAninhado(JsonElement objeto, params string[] caminho)
    {
        // Caminho de um nível só: tenta direto no objeto raiz.
        if (caminho.Length <= 2)
        {
            return Ler(objeto, caminho);
        }

        var atual = objeto;
        for (int i = 0; i < caminho.Length - 1; i++)
        {
            if (!atual.TryGetProperty(caminho[i], out var proximo) ||
                proximo.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            atual = proximo;
        }

        return Ler(atual, caminho[^1]);
    }
}
