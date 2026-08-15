using System.Diagnostics;
using System.Text.Json;
using Congrega.Domain.Retention;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Congrega.Application.Retention;

/// <summary>Resumo de um ciclo de varredura.</summary>
public sealed record RetentionScanResult
{
    public required int CandidatesScanned { get; init; }
    public required int AlertsBuilt { get; init; }
    public required int AlertsEnqueued { get; init; }
    public required int Batches { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Diferença entre construídos e enfileirados: alertas descartados pela
    /// deduplicação do banco. Em regime normal esse número é alto — cada assinatura
    /// permanece dias dentro da mesma faixa e reapresenta o mesmo alerta a cada ciclo.
    /// Ele só é sintoma de problema se for igual a <see cref="AlertsBuilt"/> por
    /// muitos ciclos seguidos, o que indicaria fila travada.
    /// </summary>
    public int AlertsDeduplicated => AlertsBuilt - AlertsEnqueued;
}

/// <summary>
/// Caso de uso do motor de retenção: varre assinaturas próximas do vencimento e
/// enfileira alertas escalonados.
/// </summary>
/// <remarks>
/// <para>
/// Puramente orquestração — não conhece EF Core, Npgsql, SMTP ou
/// <c>BackgroundService</c>. Isso permite testá-lo com dublês simples e é o que
/// mantém a inversão de dependência real, e não decorativa.
/// </para>
/// <para><b>Propriedades garantidas:</b></para>
/// <list type="number">
///   <item><description>
///     <b>Sem N+1</b> — uma query por lote devolve projeção já com nome, e-mail e
///     plano. Nenhuma propriedade de navegação é tocada dentro do laço.
///   </description></item>
///   <item><description>
///     <b>Memória limitada</b> — keyset pagination; a base inteira nunca é
///     materializada de uma vez.
///   </description></item>
///   <item><description>
///     <b>Idempotente</b> — rodar duas vezes no mesmo dia não gera alerta duplicado,
///     por conta da deduplicação no banco.
///   </description></item>
///   <item><description>
///     <b>Cancelável</b> — o <c>CancellationToken</c> é honrado entre lotes, então
///     um shutdown do pod interrompe em ponto seguro em vez de ser abortado.
///   </description></item>
/// </list>
/// </remarks>
public sealed class RetentionScanner(
    ISubscriptionRepository subscriptions,
    INotificationDispatcher dispatcher,
    TimeProvider timeProvider,
    ResiliencePipeline resiliencePipeline,
    IOptions<RetentionOptions> options,
    ILogger<RetentionScanner> logger)
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RetentionOptions _options = options.Value;

    public async Task<RetentionScanResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var today = ResolveBusinessDate();

        // Faixa varrida em uma única expressão: das assinaturas vencidas há até
        // LookBehindDays até as que vencem em LookAheadDays.
        var periodEndFrom = today.AddDays(-RetentionWindowCalculator.LookBehindDays);
        var periodEndTo = today.AddDays(RetentionWindowCalculator.LookAheadDays);

        logger.LogInformation(
            "Ciclo de retenção iniciado. Data de negócio {BusinessDate}, faixa {From}..{To}",
            today, periodEndFrom, periodEndTo);

        long cursor = 0;
        int scanned = 0, built = 0, enqueued = 0, batches = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // A resiliência envolve só a chamada ao banco. Envolver o ciclo inteiro
            // faria um retry reprocessar lotes já concluídos; envolver o lote
            // reprocessa apenas o que falhou — e é seguro porque o enfileiramento
            // é idempotente.
            var batch = await resiliencePipeline.ExecuteAsync(
                async token => await subscriptions.GetRetentionCandidatesAsync(
                    periodEndFrom, periodEndTo, cursor, _options.BatchSize, token),
                cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            batches++;
            scanned += batch.Count;

            var alerts = BuildAlerts(batch, today);
            built += alerts.Count;

            if (alerts.Count > 0)
            {
                enqueued += await resiliencePipeline.ExecuteAsync(
                    async token => await dispatcher.DispatchAsync(alerts, token),
                    cancellationToken);
            }

            // O lote vem ordenado por id da assinatura, então o último é o maior.
            cursor = batch[^1].SubscriptionId;

            if (built >= _options.MaxAlertsPerCycle)
            {
                // Teto de segurança. Um bug que jogasse 200 mil assinaturas para a
                // mesma janela viraria uma tempestade de notificação irreversível —
                // e-mail enviado não volta. Interromper e alertar é preferível.
                logger.LogWarning(
                    "Teto de {Max} alertas atingido no ciclo. Varredura interrompida — investigar.",
                    _options.MaxAlertsPerCycle);
                break;
            }

            // O LIMIT da query se aplica a ASSINATURAS, não a linhas: uma assinatura
            // de igreja com cinco administradores devolve cinco linhas. Comparar
            // batch.Count com BatchSize encerraria a varredura cedo demais e deixaria
            // assinaturas sem alerta. A contagem correta é de assinaturas distintas.
            int subscriptionsInBatch = batch.DistinctBy(c => c.SubscriptionId).Count();
            if (subscriptionsInBatch < _options.BatchSize)
            {
                break;
            }
        }

        stopwatch.Stop();

        var result = new RetentionScanResult
        {
            CandidatesScanned = scanned,
            AlertsBuilt = built,
            AlertsEnqueued = enqueued,
            Batches = batches,
            Duration = stopwatch.Elapsed
        };

        logger.LogInformation(
            "Ciclo de retenção concluído. Varridas {Scanned} em {Batches} lotes; "
            + "{Built} alertas construídos, {Enqueued} enfileirados, {Deduped} deduplicados. Duração {Duration}",
            result.CandidatesScanned, result.Batches, result.AlertsBuilt,
            result.AlertsEnqueued, result.AlertsDeduplicated, result.Duration);

        return result;
    }

    /// <summary>
    /// Converte candidatos em alertas. Sem I/O — resolve janela e faz fan-out por canal.
    /// </summary>
    private static List<RetentionAlert> BuildAlerts(
        IReadOnlyList<RetentionCandidate> candidates,
        DateOnly today)
    {
        var alerts = new List<RetentionAlert>(candidates.Count * 2);

        foreach (var candidate in candidates)
        {
            var window = RetentionWindowCalculator.Resolve(candidate.PeriodEnd, today);
            if (window is null)
            {
                continue;
            }

            var templateCode = RetentionWindowCalculator.TemplateCodeFor(window.Value);
            var payloadJson = JsonSerializer.Serialize(
                new RetentionPayload(
                    candidate.UserFullName,
                    candidate.PlanCode,
                    candidate.PeriodEnd,
                    candidate.PeriodEnd.DayNumber - today.DayNumber,
                    window.Value.ToString()),
                PayloadJsonOptions);

            foreach (var channel in RetentionWindowCalculator.ChannelsFor(window.Value))
            {
                alerts.Add(new RetentionAlert
                {
                    SubscriptionId = candidate.SubscriptionId,
                    UserId = candidate.UserId,
                    TenantId = candidate.TenantId,
                    PeriodEnd = candidate.PeriodEnd,
                    Window = window.Value,
                    Channel = channel,
                    TemplateCode = templateCode,
                    PayloadJson = payloadJson
                });
            }
        }

        return alerts;
    }

    /// <summary>
    /// Data corrente no fuso de negócio. Se o fuso configurado não existir no host,
    /// falha explicitamente em vez de cair para UTC em silêncio — o silêncio
    /// deslocaria todas as janelas em três horas sem nenhum sinal.
    /// </summary>
    private DateOnly ResolveBusinessDate()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.BusinessTimeZone);
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private sealed record RetentionPayload(
        string UserName,
        string PlanCode,
        DateOnly PeriodEnd,
        int DaysRemaining,
        string Window);
}
