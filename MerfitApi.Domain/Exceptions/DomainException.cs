namespace Merfit.Domain.Exceptions;

/// <summary>Base type for exceptions raised by domain invariant violations (mapped to HTTP 422 at the API boundary).</summary>
public abstract class DomainException(string message) : Exception(message);

public sealed class BusinessRuleViolationException(string message) : DomainException(message);

public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} with key '{key}' was not found.")
    {
    }
}

public sealed class OwnershipViolationException()
    : DomainException("The requested resource does not belong to the current user.");
