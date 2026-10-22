using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Exceptions;

/// <summary>
/// Thrown by <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership.TransitionTo"/> when
/// asked to move a membership to a status that isn't a legal next step from its current one — e.g.
/// transitioning to the same status it's already in. Both <c>Active -> Deactivated</c> and
/// <c>Deactivated -> Active</c> are legal; this is thrown for anything else. This is a Domain-layer
/// invariant violation, not an HTTP concern — callers in the Application layer (command handlers)
/// are responsible for catching this and translating it into whatever API-facing
/// exception/response their use case contract calls for.
/// </summary>
public class InvalidUserStatusTransitionException : Exception
{
    public UserStatus CurrentStatus { get; }
    public UserStatus TargetStatus { get; }

    public InvalidUserStatusTransitionException(UserStatus currentStatus, UserStatus targetStatus)
        : base($"Cannot transition a user from {currentStatus} to {targetStatus}.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }
}
