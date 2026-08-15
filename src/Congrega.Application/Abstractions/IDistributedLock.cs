namespace Congrega.Application.Abstractions;

/// <summary>
/// Lock distribuído entre réplicas.
/// </summary>
/// <remarks>
/// <para>
/// Existe para evitar <b>trabalho duplicado</b> quando o Deployment tem mais de uma
/// réplica — não para garantir correção. A correção do motor de retenção vem da
/// constraint <c>UNIQUE (dedupe_key)</c> em <c>notification_queue</c>.
/// </para>
/// <para>
/// A distinção é importante e frequentemente ignorada: locks distribuídos falham de
/// formas silenciosas (partição de rede, expiração por GC pause, sessão devolvida ao
/// pool). Um sistema cuja correção depende do lock quebra nesses cenários; um sistema
/// que usa o lock apenas como otimização apenas desperdiça trabalho. Ver ADR-021.
/// </para>
/// </remarks>
public interface IDistributedLock
{
    /// <summary>
    /// Tenta adquirir o lock sem bloquear.
    /// </summary>
    /// <returns>
    /// Um handle a ser descartado para liberar, ou <c>null</c> se outra réplica já o
    /// detém. Não lança quando o lock está tomado — não conseguir o lock é um
    /// resultado normal do ciclo, não uma condição de erro.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquireAsync(string lockKey, CancellationToken cancellationToken);
}
