using Congrega.Application.Abstractions;
using Congrega.Application.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Congrega.Workers;

/// <summary>
/// Hospeda o motor de retenção: dispara o ciclo periodicamente, garante execução
/// única entre réplicas e isola falhas para que nenhuma delas derrube o host.
/// </summary>
/// <remarks>
/// A responsabilidade aqui é <b>só</b> de hospedagem. Nenhuma regra de negócio: a
/// lógica está em <see cref="RetentionScanner"/> (aplicação) e em
/// <c>RetentionWindowCalculator</c> (domínio), ambos testáveis sem host, sem banco e
/// sem relógio real.
/// </remarks>
public sealed class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock,
    IOptions<RetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionBackgroundService> logger) : BackgroundService
{
    private readonly RetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Motor de retenção desabilitado por configuração. Nenhum ciclo será executado.");
            return;
        }

        // Jitter inicial. Sem ele, todas as réplicas sobem juntas após um deploy e
        // disputam o mesmo lock no mesmo instante — uma vence e as demais fazem uma
        // ida ao banco inútil, em rajada. Espalhar o início elimina o efeito manada.
        var initialDelay = TimeSpan.FromSeconds(Random.Shared.Next(0, 30));
        logger.LogInformation(
            "Motor de retenção iniciando em {Delay} (jitter). Intervalo do ciclo: {Interval}.",
            initialDelay, _options.Interval);

        try
        {
            await Task.Delay(initialDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval, timeProvider);

        // do/while: executa um ciclo imediatamente na subida, em vez de esperar o
        // primeiro tick. Após um deploy, alertas do dia saem na hora.
        do
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Motor de retenção encerrado por shutdown.");
                break;
            }
            catch (Exception ex)
            {
                // A regra que o briefing exige: exceção no ciclo NÃO derruba o host.
                // Um BackgroundService cuja ExecuteAsync propaga exceção encerra o
                // host inteiro por padrão — e levaria junto todos os outros workers
                // do processo. Registrar e aguardar o próximo tick é o comportamento
                // correto: falha transitória se resolve sozinha, falha permanente
                // aparece no alerta de ciclos consecutivos com erro.
                logger.LogError(ex, "Falha no ciclo de retenção. O próximo ciclo seguirá normalmente.");
            }
        }
        while (await SafeWaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        // O lock evita trabalho duplicado entre réplicas. Ele NÃO é a garantia de
        // não duplicar alerta — essa vem do UNIQUE (dedupe_key). Ver ADR-021.
        await using var handle = await distributedLock.TryAcquireAsync(_options.LockKey, cancellationToken);

        if (handle is null)
        {
            logger.LogDebug("Outra réplica está executando o ciclo. Ignorando este tick.");
            return;
        }

        // Escopo próprio por ciclo: o scanner e suas dependências são scoped, e um
        // BackgroundService é singleton. Resolver serviços scoped a partir do
        // provider raiz é o vazamento clássico de DbContext em worker.
        await using var scope = scopeFactory.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<RetentionScanner>();

        var result = await scanner.ExecuteAsync(cancellationToken);

        if (result.AlertsEnqueued > 0)
        {
            logger.LogInformation(
                "Ciclo concluído com {Enqueued} novos alertas enfileirados.", result.AlertsEnqueued);
        }
    }

    /// <summary>
    /// Aguarda o próximo tick tratando o cancelamento como fim normal, e não como erro.
    /// </summary>
    private static async Task<bool> SafeWaitForNextTickAsync(
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
