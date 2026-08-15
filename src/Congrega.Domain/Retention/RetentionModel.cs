using System.Globalization;
using Congrega.Domain.Billing;

namespace Congrega.Domain.Retention;

/// <summary>
/// Janelas de alerta escalonadas do motor de retenção.
/// O valor numérico é o número de dias restantes que dá nome à janela — negativo
/// após o vencimento. Manter essa correspondência facilita a leitura de logs.
/// </summary>
public enum RetentionAlertWindow
{
    /// <summary>15 dias antes do vencimento.</summary>
    D15 = 15,

    /// <summary>7 dias antes.</summary>
    D7 = 7,

    /// <summary>3 dias antes.</summary>
    D3 = 3,

    /// <summary>1 dia antes ou no próprio dia.</summary>
    D1 = 1,

    /// <summary>3 dias após o vencimento, dentro do grace period.</summary>
    GraceD3 = -3
}

/// <summary>Canal de entrega da notificação.</summary>
public enum NotificationChannel
{
    Email = 1,
    Push = 2,
    InAppBanner = 3
}

/// <summary>
/// Projeção somente-leitura de um par (assinatura, destinatário) candidato a alerta.
/// </summary>
/// <remarks>
/// Uma assinatura pessoal (Congrega+) produz exatamente uma linha: o próprio
/// assinante. Uma assinatura de igreja produz uma linha por administrador ativo
/// daquele tenant — é por isso que a granularidade desta projeção é o destinatário,
/// e não a assinatura.
/// </remarks>
/// <remarks>
/// Deliberadamente <b>não</b> é a entidade <see cref="Subscription"/>. O worker
/// varre milhares de linhas por ciclo e precisa apenas dos campos abaixo; carregar
/// agregados rastreados pelo EF Core custaria memória e change tracking sem
/// nenhum ganho — e é a origem clássica de N+1 quando alguém navega uma propriedade
/// de navegação dentro do laço.
/// </remarks>
public sealed record RetentionCandidate
{
    public required long SubscriptionId { get; init; }
    public required long UserId { get; init; }
    public long? TenantId { get; init; }
    public required string UserEmail { get; init; }
    public required string UserFullName { get; init; }
    public required string PlanCode { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required SubscriptionStatus Status { get; init; }
}

/// <summary>
/// Um alerta pronto para ser enfileirado: destinatário, canal, template e chave
/// de deduplicação já resolvidos.
/// </summary>
public sealed record RetentionAlert
{
    public required long SubscriptionId { get; init; }
    public required long UserId { get; init; }
    public long? TenantId { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required RetentionAlertWindow Window { get; init; }
    public required NotificationChannel Channel { get; init; }
    public required string TemplateCode { get; init; }
    public required string PayloadJson { get; init; }

    /// <summary>
    /// Chave de deduplicação persistida com <c>UNIQUE</c> em
    /// <c>notification_queue.dedupe_key</c>.
    /// </summary>
    /// <remarks>
    /// <para>Três componentes merecem justificativa:</para>
    /// <para>
    /// <b><c>PeriodEnd</c></b> — sem ele, uma assinatura renovada jamais receberia
    /// alerta de novo: a chave do ciclo anterior ocuparia o lugar para sempre.
    /// Incluí-lo faz a chave se renovar naturalmente a cada ciclo de cobrança.
    /// </para>
    /// <para>
    /// <b><c>Channel</c></b> — a mesma janela dispara em até três canais. Sem o
    /// canal na chave, e-mail, push e banner colidiriam entre si e apenas o
    /// primeiro seria entregue.
    /// </para>
    /// <para>
    /// <b><c>UserId</c></b> — uma assinatura de igreja (B2B) tem vários
    /// destinatários: todos os administradores daquele tenant. Sem o usuário na
    /// chave, os administradores colidiriam entre si e apenas o primeiro seria
    /// avisado do vencimento — silenciosamente, e justamente no produto que gera a
    /// receita B2B. O requisito do briefing é que <i>um mesmo usuário</i> não receba
    /// o mesmo alerta duas vezes; a chave precisa refletir isso literalmente.
    /// </para>
    /// </remarks>
    public string DedupeKey =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"retention:{SubscriptionId}:{UserId}:{PeriodEnd:yyyy-MM-dd}:{Window}:{Channel}");
}
