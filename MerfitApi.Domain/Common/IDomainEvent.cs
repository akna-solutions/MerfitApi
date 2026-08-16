namespace Merfit.Domain.Common;

/// <summary>
/// Marker for domain events raised by aggregates and dispatched after SaveChanges succeeds.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract class Entity : BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
