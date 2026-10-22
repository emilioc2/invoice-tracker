using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Exceptions;

/// <summary>
/// Thrown by <see cref="InvoiceTracker.Domain.Entities.Invoice.TransitionTo"/> when asked to move
/// an invoice to a status that isn't a legal next step from its current one (per
/// requirements.md's status workflow: Draft -> Unpaid -> Overdue -> Paid, Draft/Unpaid ->
/// Cancelled). This is a Domain-layer invariant violation, not an HTTP concern — callers in the
/// Application layer (command handlers) are responsible for catching this and translating it into
/// whatever API-facing exception/response their use case contract calls for (typically a 400).
/// </summary>
public class InvalidInvoiceStatusTransitionException : Exception
{
    public InvoiceStatus CurrentStatus { get; }
    public InvoiceStatus TargetStatus { get; }

    public InvalidInvoiceStatusTransitionException(InvoiceStatus currentStatus, InvoiceStatus targetStatus)
        : base($"Cannot transition an invoice from {currentStatus} to {targetStatus}.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }
}
