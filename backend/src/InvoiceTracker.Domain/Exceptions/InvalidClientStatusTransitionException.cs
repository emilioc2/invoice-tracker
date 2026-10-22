using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Exceptions;

/// <summary>
/// Thrown by <see cref="InvoiceTracker.Domain.Entities.Client.TransitionTo"/> when asked to move
/// a client to a status that isn't a legal next step from its current one (per
/// requirements.md's Client entity description: the only legal transition is
/// Active -> Removed; there is no reverse transition). This is a Domain-layer invariant
/// violation, not an HTTP concern — callers in the Application layer (command handlers) are
/// responsible for catching this and translating it into whatever API-facing exception/response
/// their use case contract calls for.
/// </summary>
public class InvalidClientStatusTransitionException : Exception
{
    public ClientStatus CurrentStatus { get; }
    public ClientStatus TargetStatus { get; }

    public InvalidClientStatusTransitionException(ClientStatus currentStatus, ClientStatus targetStatus)
        : base($"Cannot transition a client from {currentStatus} to {targetStatus}.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }
}
