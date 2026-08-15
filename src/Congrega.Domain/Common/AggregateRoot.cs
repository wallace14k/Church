namespace Congrega.Domain.Common;

/// <summary>Evento de domínio. Marcador — sem dependência de infraestrutura.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raiz de agregado. Acumula eventos de domínio que a camada de persistência drena
/// e grava no Outbox <b>na mesma transação</b> da mudança de estado.
/// </summary>
/// <remarks>
/// O agregado não publica nada por conta própria e não conhece o Outbox: ele apenas
/// registra o que aconteceu. É essa ignorância que mantém o domínio livre de
/// infraestrutura — requisito da Clean Architecture que o briefing exige e que a
/// "Clean Architecture de fachada" costuma violar logo aqui.
/// </remarks>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Chamado pela camada de persistência após gravar os eventos no Outbox.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
