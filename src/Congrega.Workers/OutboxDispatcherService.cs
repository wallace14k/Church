using Congrega.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Congrega.Workers;

/// <summary>
/// Hospeda o dispatcher do Outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sem lock distribuído, diferente do motor de retenção.</b> A reivindicação
/// usa <c>FOR UPDATE SKIP LOCKED</c> com lease, então cada réplica leva um
/// conjunto disjunto de mensagens e todas processam em paralelo. Quando o
/// trabalho é particionável, particiona-se — serializar aqui só reduziria a
/// vazão.
/// </para>
/// <para>
/// <b>Ciclo curto.</b> Cinco segundos, contra uma hora da retenção. A mensagem
/// mais importante desta fila é o código OTP, e o usuário está olhando para a
/// tela esperando o e-mail chegar. Um intervalo longo seria eficiente e péssimo.
/// </para>
/// <para>
/// <b>Drenagem contínua.</b> Se um ciclo encheu o lote, o próximo roda sem
/// esperar o tick: uma fila represada — depois de um incidente no provedor, por
/// exemplo — precisa esvaziar rápido, e não a cinquenta mensagens a cada cinco
/// segundos.
/// </para>
/// </remarks>
public sealed class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Dispatcher do Outbox desabilitado por configuração. Nada será entregue.");
            return;
        }

        logger.LogInformation(
            "Dispatcher do Outbox iniciado. Intervalo {Interval}, lote {BatchSize}, lease {Lease}.",
            _options.Interval, _options.BatchSize, _options.LeaseDuration);

        using var timer = new PeriodicTimer(_options.Interval, timeProvider);

        do
        {
            try
            {
                // Enquanto houver fila cheia, continua drenando sem esperar o tick.
                // O teto de iterações evita monopolizar o processo caso a fila
                // esteja crescendo mais rápido do que conseguimos esvaziar.
                for (int rodada = 0; rodada < 20 && !stoppingToken.IsCancellationRequested; rodada++)
                {
                    var resultado = await ExecutarCicloAsync(stoppingToken);

                    if (resultado.Claimed < _options.BatchSize)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Dispatcher do Outbox encerrado por shutdown.");
                break;
            }
            catch (Exception ex)
            {
                // Exceção no ciclo NÃO derruba o host. Um BackgroundService cuja
                // ExecuteAsync propaga exceção encerra o processo inteiro por
                // padrão, e levaria junto o motor de retenção.
                logger.LogError(ex, "Falha no ciclo do Outbox. O próximo ciclo seguirá normalmente.");
            }
        }
        while (await AguardarProximoAsync(timer, stoppingToken));
    }

    private async Task<OutboxProcessResult> ExecutarCicloAsync(CancellationToken cancellationToken)
    {
        // Escopo próprio por ciclo: o processador e seus handlers são scoped, e um
        // BackgroundService é singleton. Resolver serviços scoped a partir do
        // provider raiz é o vazamento clássico de conexão em worker.
        await using var escopo = scopeFactory.CreateAsyncScope();
        var processador = escopo.ServiceProvider.GetRequiredService<OutboxProcessor>();

        return await processador.ProcessBatchAsync(_options, cancellationToken);
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
