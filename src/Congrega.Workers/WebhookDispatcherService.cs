using Congrega.Application.Billing;
using Congrega.Domain.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Congrega.Workers;

/// <summary>
/// Hospeda o processamento assíncrono de webhooks de pagamento.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo desenho do <see cref="OutboxDispatcherService"/>: reivindicação por
/// <c>FOR UPDATE SKIP LOCKED</c> — sem lock distribuído, porque cada réplica
/// leva um conjunto disjunto de eventos —, ciclo curto com
/// <see cref="PeriodicTimer"/>, e drenagem contínua enquanto o lote vier cheio.
/// </para>
/// <para>
/// Diferente do Outbox, não há um dicionário de handlers por tipo de mensagem:
/// todo evento reivindicado passa por <see cref="ProcessPaymentWebhookHandler"/>,
/// que já sabe resolver qualquer evento de pagamento contra o gateway
/// (fetch-on-notify). Um único tipo de fila, um único processador.
/// </para>
/// </remarks>
public sealed class WebhookDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookProcessorOptions> options,
    TimeProvider timeProvider,
    ILogger<WebhookDispatcherService> logger) : BackgroundService
{
    private readonly WebhookProcessorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Processador de webhooks de pagamento desabilitado por configuração.");
            return;
        }

        logger.LogInformation(
            "Processador de webhooks de pagamento iniciado. Intervalo {Interval}, lote {BatchSize}.",
            _options.Interval, _options.BatchSize);

        using var timer = new PeriodicTimer(_options.Interval, timeProvider);

        do
        {
            try
            {
                // Enquanto houver fila cheia, continua drenando sem esperar o tick —
                // mesmo motivo do Outbox: uma fila represada precisa esvaziar rápido.
                for (int rodada = 0; rodada < 20 && !stoppingToken.IsCancellationRequested; rodada++)
                {
                    int reivindicados = await ExecutarCicloAsync(stoppingToken);

                    if (reivindicados < _options.BatchSize)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Processador de webhooks de pagamento encerrado por shutdown.");
                break;
            }
            catch (Exception ex)
            {
                // Exceção no ciclo NÃO derruba o host — levaria junto a retenção e o
                // dispatcher do Outbox, que rodam no mesmo processo.
                logger.LogError(
                    ex, "Falha no ciclo do processador de webhooks. O próximo ciclo seguirá normalmente.");
            }
        }
        while (await AguardarProximoAsync(timer, stoppingToken));
    }

    private async Task<int> ExecutarCicloAsync(CancellationToken cancellationToken)
    {
        // Escopo próprio por ciclo: o handler e seus repositórios são scoped, e
        // este BackgroundService é singleton.
        await using var escopo = scopeFactory.CreateAsyncScope();
        var webhooks = escopo.ServiceProvider.GetRequiredService<IPaymentWebhookRepository>();
        var handler = escopo.ServiceProvider.GetRequiredService<ProcessPaymentWebhookHandler>();

        var lote = await webhooks.ClaimBatchAsync(_options.BatchSize, _options.MaxAttempts, cancellationToken);

        int processados = 0, ignorados = 0, falhados = 0;

        foreach (var evento in lote)
        {
            // Cancelamento entre eventos, nunca no meio de um: o que está em voo
            // termina ou falha; os demais voltam a ficar disponíveis na próxima
            // reivindicação, já que não há lease aqui.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var resultado = await handler.HandleAsync(evento, cancellationToken);

            switch (resultado.Outcome)
            {
                case WebhookOutcome.Processed: processados++; break;
                case WebhookOutcome.Ignored: ignorados++; break;
                case WebhookOutcome.Failed: falhados++; break;
            }
        }

        if (lote.Count > 0)
        {
            logger.LogInformation(
                "Webhooks de pagamento: {Claimed} reivindicados, {Processed} processados, "
                + "{Ignored} ignorados, {Failed} com falha.",
                lote.Count, processados, ignorados, falhados);
        }

        return lote.Count;
    }

    private static async Task<bool> AguardarProximoAsync(
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
