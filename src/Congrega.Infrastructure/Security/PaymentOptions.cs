using System.ComponentModel.DataAnnotations;

namespace Congrega.Infrastructure.Security;

/// <summary>
/// Configuração do gateway de pagamento e da verificação de webhook.
/// </summary>
/// <remarks>
/// <para>
/// <b>Segredo nunca no repositório.</b> Vem de User Secrets em desenvolvimento e
/// do cofre do ambiente em produção — o mesmo caminho já usado por
/// <c>SigningKeyPem</c> e <c>OtpPepper</c>.
/// </para>
/// </remarks>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Segredo compartilhado do HMAC do webhook.
    /// </summary>
    /// <remarks>
    /// Sem ele, <see cref="WebhookSignatureVerifier"/> recusa <b>tudo</b> — e é
    /// esse o comportamento correto: um endpoint de webhook sem verificação de
    /// assinatura aceita "pagamento confirmado" de qualquer um na internet.
    /// Falhar fechado é a única opção defensável aqui.
    /// </remarks>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    /// Janela de tolerância do timestamp do webhook.
    /// </summary>
    /// <remarks>
    /// Cinco minutos é o padrão da indústria: folgado o bastante para diferença
    /// de relógio entre servidores, curto o bastante para que um evento
    /// capturado não sirva de replay. Aumentar isso alarga exatamente a janela
    /// de ataque.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:30", "00:30:00")]
    public TimeSpan WebhookTolerance { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>URL base da API do provedor. Vazia em desenvolvimento, que usa o adaptador falso.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Chave de API do provedor.</summary>
    public string ApiKey { get; init; } = string.Empty;
}
