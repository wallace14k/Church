namespace Congrega.Domain.Retention;

/// <summary>
/// Acesso a assinaturas para o motor de retenção.
/// </summary>
/// <remarks>
/// <para>
/// Interface com <b>intenção de domínio</b>, não um <c>IRepository&lt;T&gt;</c>
/// genérico — anti-padrão explicitamente vedado pelo briefing. Ela expõe a única
/// pergunta que o motor de retenção precisa fazer, e por isso pode ser implementada
/// com a query exata que essa pergunta merece.
/// </para>
/// <para>
/// Um repositório genérico forçaria o caso de uso a compor <c>IQueryable</c> e
/// vazaria EF Core para dentro da camada de aplicação; aqui a assinatura do método
/// é o contrato inteiro.
/// </para>
/// </remarks>
public interface ISubscriptionRepository
{
    /// <summary>
    /// Devolve, em lote e ordenado por id, as assinaturas cujo fim de período cai na
    /// faixa informada e cujo estado ainda admite renovação.
    /// </summary>
    /// <param name="periodEndFrom">Início da faixa de <c>current_period_end</c>.</param>
    /// <param name="periodEndTo">Fim da faixa.</param>
    /// <param name="afterSubscriptionId">
    /// Cursor de keyset pagination: devolve apenas ids maiores que este.
    /// Keyset e não <c>OFFSET</c> — com offset, a última página de uma varredura de
    /// 200 mil linhas obriga o Postgres a descartar todas as anteriores a cada lote.
    /// </param>
    /// <param name="batchSize">Tamanho máximo do lote.</param>
    Task<IReadOnlyList<RetentionCandidate>> GetRetentionCandidatesAsync(
        DateOnly periodEndFrom,
        DateOnly periodEndTo,
        long afterSubscriptionId,
        int batchSize,
        CancellationToken cancellationToken);
}

/// <summary>
/// Enfileira alertas para envio posterior.
/// </summary>
/// <remarks>
/// O nome é <c>Dispatch</c>, mas o contrato é explicitamente de <b>enfileiramento</b>:
/// nenhuma implementação deve chamar provedor de e-mail ou push aqui dentro. O job
/// grava na fila e no Outbox dentro da transação; quem entrega é outro worker.
/// <para>
/// Enviar e-mail dentro do job traria dois defeitos conhecidos: a latência do
/// provedor entraria no tempo do ciclo (segurando o lock distribuído), e uma falha
/// após o commit deixaria o alerta marcado como enviado sem ter sido — exatamente o
/// problema que o Outbox Pattern existe para resolver.
/// </para>
/// </remarks>
public interface INotificationDispatcher
{
    /// <summary>
    /// Enfileira o conjunto de alertas de forma idempotente.
    /// </summary>
    /// <returns>
    /// Quantidade <b>efetivamente</b> enfileirada. Alertas já existentes (mesma
    /// <see cref="RetentionAlert.DedupeKey"/>) são descartados pelo banco e não
    /// entram nesta contagem — é assim que a deduplicação se torna observável.
    /// </returns>
    Task<int> DispatchAsync(
        IReadOnlyCollection<RetentionAlert> alerts,
        CancellationToken cancellationToken);
}
