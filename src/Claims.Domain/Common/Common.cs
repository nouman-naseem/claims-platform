using Claims.Domain.Enums;

namespace Claims.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>A business rule was violated.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>The requested lifecycle transition is not allowed from the current state.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(ClaimStatus from, ClaimStatus to)
        : base($"A claim cannot move from {from} to {to}.") { }
}

/// <summary>
/// Integration-relevant fact raised by an aggregate. Persisted to the outbox in the
/// same transaction as the aggregate change (see ClaimsDbContext).
/// </summary>
public sealed record DomainEvent(
    string EventType,
    Guid ClaimId,
    string ClaimNumber,
    DateTime OccurredAt,
    IReadOnlyDictionary<string, object?> Data);
