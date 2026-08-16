using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Congrega.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Congrega.Application.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Intervalo entre ciclos.
    /// </summary>
    /// <remarks>
    /// Cinco segundos porque a mensagem mais importante da fila é o código OTP, e
    /// o usuário está olhando para a tela esperando o e-mail chegar. Um minuto
    /// seria eficiente e péssimo.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Tempo de lease de uma mensagem reivindicada.
    /// </summary>
    /// <remarks>
    /// Precisa ser maior que o pior tempo de processamento e menor que a
    /// paciência para reprocessar depois de um pod cair. Curto demais faz duas
    /// réplicas pegarem a mesma mensagem; longo demais deixa a mensagem parada.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Tentativas antes do dead letter.
    /// </summary>
    /// <remarks>
    /// Com backoff de base 30s, seis tentativas cobrem cerca de meia hora —
    /// suficiente para um provedor se recuperar de instabilidade curta, sem
    /// manter a fila girando por dias sobre algo que não vai passar.
    /// </remarks>
    [Range(1, 20)]
    public short MaxAttempts { get; init; } = 6;

    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record OutboxProcessResult
{
    public required int Claimed { get; init; }
    public required int Processed { get; init; }
    public required int Retried { get; init; }
    public required int DeadLettered { get; init; }
}

/// <summary>
/// Drena a fila do Outbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não há lock distribuído aqui, diferente do motor de retenção.</b>
/// A reivindicação usa <c>FOR UPDATE SKIP LOCKED</c> com lease: cada réplica
/// leva um conjunto <i>disjunto</i> de mensagens, sem coordenação. Isso é melhor
/// que um lock — as réplicas processam em paralelo em vez de esperar a vez, e a
/// vazão cresce com o número de pods.
/// </para>
/// <para>
/// A retenção precisa de lock porque a varredura é uma operação global sobre a
/// mesma faixa de dados; aqui cada mensagem é uma unidade independente. Quando o
/// trabalho é particionável, particione — não serialize.
/// </para>
/// <para>
/// <b>Isolamento de mensagem venenosa.</b> Cada mensagem é processada no próprio
/// <c>try</c>. Uma que sempre falha consome as tentativas dela e vai para dead
/// letter sem bloquear a fila. Sem esse isolamento, um payload malformado
/// pararia todos os OTPs da plataforma.
/// </para>
/// </remarks>
public sealed class OutboxProcessor(
    IOutboxStore store,
    IEnumerable<IOutboxMessageHandler> handlers,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    private readonly Dictionary<string, IOutboxMessageHandler> _handlers =
        handlers.ToDictionary(h => h.MessageType, StringComparer.Ordinal);

    public async Task<OutboxProcessResult> ProcessBatchAsync(
        OutboxOptions options,
        CancellationToken cancellationToken)
    {
        var lote = await store.ClaimBatchAsync(options.BatchSize, options.LeaseDuration, cancellationToken);

        int processadas = 0, reagendadas = 0, descartadas = 0;

        foreach (var mensagem in lote)
        {
            // Cancelamento entre mensagens, nunca no meio de uma. A que está em voo
            // termina ou falha; as demais permanecem reivindicadas e voltam à fila
            // quando o lease expirar.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Restaura o correlation ID da requisição que originou a mensagem — é o
            // que permite rastrear, minutos depois, o e-mail até o toque na tela.
            using var atividade = new Activity("outbox.process")
                .SetParentId(mensagem.CorrelationId ?? string.Empty)
                .Start();

            switch (await ProcessarUmaAsync(mensagem, options, cancellationToken))
            {
                case Desfecho.Processada: processadas++; break;
                case Desfecho.Reagendada: reagendadas++; break;
                case Desfecho.Descartada: descartadas++; break;
            }
        }

        if (lote.Count > 0)
        {
            logger.LogInformation(
                "Outbox: {Claimed} reivindicadas, {Processed} processadas, "
                + "{Retried} reagendadas, {DeadLettered} em dead letter.",
                lote.Count, processadas, reagendadas, descartadas);
        }

        return new OutboxProcessResult
        {
            Claimed = lote.Count,
            Processed = processadas,
            Retried = reagendadas,
            DeadLettered = descartadas,
        };
    }

    private async Task<Desfecho> ProcessarUmaAsync(
        OutboxEnvelope mensagem,
        OutboxOptions options,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(mensagem.MessageType, out var handler))
        {
            // Tipo sem handler é erro de configuração, não falha transitória:
            // nenhuma tentativa futura vai encontrar um handler que não foi
            // registrado.
            logger.LogError(
                "Mensagem {MessageId} do tipo {MessageType} não tem handler registrado.",
                mensagem.Id, mensagem.MessageType);

            await store.MarkDeadLetteredAsync(
                mensagem.Id, $"Nenhum handler registrado para '{mensagem.MessageType}'.", cancellationToken);

            return Desfecho.Descartada;
        }

        try
        {
            await handler.HandleAsync(mensagem.Payload, cancellationToken);
            await store.MarkProcessedAsync(mensagem.Id, cancellationToken);

            // Log sem o payload: mensagens de OTP carregam o código em texto plano,
            // e registrá-lo anularia todo o cuidado de guardar só o hash no banco.
            logger.LogDebug(
                "Mensagem {MessageId} ({MessageType}) processada.", mensagem.Id, mensagem.MessageType);

            return Desfecho.Processada;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown do pod não é falha da mensagem. Deixa o lease expirar e outra
            // réplica pega. Contar como tentativa puniria a mensagem por um deploy.
            throw;
        }
        catch (PermanentDeliveryException ex)
        {
            logger.LogWarning(ex,
                "Falha permanente na mensagem {MessageId} ({MessageType}). Enviando para dead letter.",
                mensagem.Id, mensagem.MessageType);

            await store.MarkDeadLetteredAsync(mensagem.Id, Descrever(ex), cancellationToken);
            return Desfecho.Descartada;
        }
        catch (Exception ex)
        {
            if (mensagem.Attempts >= options.MaxAttempts)
            {
                logger.LogError(ex,
                    "Mensagem {MessageId} ({MessageType}) esgotou {Attempts} tentativas.",
                    mensagem.Id, mensagem.MessageType, mensagem.Attempts);

                await store.MarkDeadLetteredAsync(mensagem.Id, Descrever(ex), cancellationToken);
                return Desfecho.Descartada;
            }

            var proxima = timeProvider.GetUtcNow().Add(CalcularBackoff(options, mensagem.Attempts));

            logger.LogWarning(ex,
                "Falha transitória na mensagem {MessageId} ({MessageType}), tentativa {Attempts}. "
                + "Próxima em {NextAttempt}.",
                mensagem.Id, mensagem.MessageType, mensagem.Attempts, proxima);

            await store.MarkForRetryAsync(mensagem.Id, Descrever(ex), proxima, cancellationToken);
            return Desfecho.Reagendada;
        }
    }

    /// <summary>
    /// Backoff exponencial com jitter.
    /// </summary>
    /// <remarks>
    /// O jitter não é enfeite: sem ele, cem mensagens que falham juntas por uma
    /// queda do provedor voltam a bater nele exatamente no mesmo instante,
    /// repetidas vezes — e a rajada atrasa a recuperação que se está esperando.
    /// </remarks>
    internal static TimeSpan CalcularBackoff(OutboxOptions options, short tentativas)
    {
        double segundos = options.BaseBackoff.TotalSeconds * Math.Pow(2, Math.Max(0, tentativas - 1));
        double jitter = Random.Shared.NextDouble() * 0.3 * segundos;   // até +30%

        return TimeSpan.FromSeconds(Math.Min(segundos + jitter, TimeSpan.FromHours(1).TotalSeconds));
    }

    /// <summary>
    /// Descrição curta do erro para a coluna <c>last_error</c>.
    /// </summary>
    /// <remarks>
    /// Sem stack trace: a coluna existe para o operador entender o que houve, e um
    /// stack de 4 KB por linha infla a tabela sem acrescentar nada que o log
    /// estruturado já não tenha — com o correlation ID para ligar os dois.
    /// </remarks>
    private static string Descrever(Exception ex)
    {
        string texto = $"{ex.GetType().Name}: {ex.Message}";
        return texto.Length <= 500 ? texto : texto[..500];
    }

    private enum Desfecho
    {
        Processada,
        Reagendada,
        Descartada,
    }
}
