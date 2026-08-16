namespace Congrega.Application.Outbox;

/// <summary>Mensagem do Outbox reivindicada para processamento.</summary>
public sealed record OutboxEnvelope
{
    public required long Id { get; init; }
    public required string MessageType { get; init; }
    public required string Payload { get; init; }

    /// <summary>Tentativas já consumidas, incluindo a corrente.</summary>
    public required short Attempts { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>
/// Acesso à fila do Outbox.
/// </summary>
/// <remarks>
/// O contrato é de <b>lease</b>, não de leitura: reivindicar uma mensagem já a
/// tira da fila por um intervalo. Um <c>SELECT</c> puro permitiria que duas
/// réplicas pegassem a mesma mensagem e enviassem o mesmo e-mail duas vezes.
/// </remarks>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(long messageId, CancellationToken cancellationToken);

    Task MarkForRetryAsync(
        long messageId,
        string failure,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retira a mensagem da fila em definitivo.
    /// </summary>
    /// <remarks>
    /// A mensagem <b>não</b> é apagada: fica registrada com o erro, para
    /// investigação e eventual reprocessamento manual. Apagar destruiria a
    /// evidência do problema junto com o problema.
    /// </remarks>
    Task MarkDeadLetteredAsync(long messageId, string failure, CancellationToken cancellationToken);
}

/// <summary>
/// Processa um tipo de mensagem do Outbox.
/// </summary>
/// <remarks>
/// <para>
/// Implementações devem ser <b>idempotentes</b>. O Outbox garante entrega
/// <i>ao menos uma vez</i>, nunca exatamente uma: uma falha entre o envio
/// bem-sucedido e a marcação de processada faz a mensagem voltar. Handler que
/// não tolera repetição vai duplicar em produção, e o cenário é comum.
/// </para>
/// <para>
/// Sinalização de erro: <c>TransientDeliveryException</c> para reagendar,
/// <c>PermanentDeliveryException</c> para dead letter. Qualquer outra exceção é
/// tratada como transitória — o palpite conservador, porque desistir de uma
/// mensagem por engano perde dado, enquanto tentar de novo por engano custa uma
/// requisição.
/// </para>
/// </remarks>
public interface IOutboxMessageHandler
{
    /// <summary>Tipo tratado, como gravado em <c>outbox_messages.message_type</c>.</summary>
    string MessageType { get; }

    Task HandleAsync(string payloadJson, CancellationToken cancellationToken);
}

/// <summary>Contato do usuário, para envios fora do fluxo de autenticação.</summary>
public sealed record UserContact
{
    public required string Email { get; init; }
    public required string FullName { get; init; }
}

public interface IUserContactResolver
{
    /// <summary>Devolve <c>null</c> para conta anonimizada ou bloqueada.</summary>
    Task<UserContact?> FindAsync(long userId, CancellationToken cancellationToken);
}

public sealed record SecurityEventRecord
{
    public required string EventType { get; init; }
    public long? UserId { get; init; }
    public required short Severity { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Metadata { get; init; }
}

public interface ISecurityEventStore
{
    Task RecordAsync(SecurityEventRecord record, CancellationToken cancellationToken);
}
