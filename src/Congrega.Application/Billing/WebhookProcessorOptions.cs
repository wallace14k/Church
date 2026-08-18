using System.ComponentModel.DataAnnotations;

namespace Congrega.Application.Billing;

public sealed class WebhookProcessorOptions
{
    public const string SectionName = "WebhookProcessor";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Intervalo entre ciclos.
    /// </summary>
    /// <remarks>
    /// Mais folgado que o Outbox (5s): o usuário não está com o dedo no botão
    /// esperando o webhook — normalmente já foi redirecionado para uma tela de
    /// "processando". Ainda assim curto o bastante para não deixar uma
    /// confirmação de pagamento pendurada por minutos.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Tentativas antes de a linha parar de ser reivindicada.
    /// </summary>
    /// <remarks>
    /// Diferente do Outbox, não há coluna de backoff em <c>payment_webhooks</c>
    /// — o intervalo entre tentativas é o próprio ciclo do dispatcher, sem
    /// crescimento exponencial. O evento nunca é apagado; só some da
    /// reivindicação, com <c>last_error</c> preenchido para investigação.
    /// </remarks>
    [Range(1, 20)]
    public short MaxAttempts { get; init; } = 6;
}
