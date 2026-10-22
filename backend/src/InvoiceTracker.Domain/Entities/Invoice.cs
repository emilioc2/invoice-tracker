using InvoiceTracker.Domain.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

namespace InvoiceTracker.Domain.Entities;

public class Invoice : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public Guid ClientId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime CreatedAtDate { get; set; }

    // null while Draft; set automatically (and becomes immutable) the moment
    // the invoice is sent (Draft -> Unpaid). Never directly editable via PUT.
    public DateTime? IssueDate { get; set; }
    public DateTime DueDate { get; set; }

    // Private setter: every status change must go through TransitionTo below, which is the one
    // place the Draft->Unpaid->Overdue->Paid/Cancelled legality rules live (see architecture.md's
    // "Domain invariants: centralized status transitions"). Starts at Draft, matching every
    // invoice's actual starting state (CreateInvoiceCommandHandler relies on this default rather
    // than assigning it explicitly, since it can no longer set it from outside the class).
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTime LastModifiedDate { get; set; }

    // null = active, non-null = archived. Never hard-deleted.
    public DateTime? ArchivedAt { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();

    /// <summary>
    /// The single place the invoice status workflow's legal transitions are encoded (per
    /// requirements.md's "Status workflow": Draft -> Unpaid, Unpaid -> Overdue, Unpaid/Overdue ->
    /// Paid, Draft/Unpaid -> Cancelled). Every caller that changes <see cref="Status"/> — command
    /// handlers and the overdue scanner background service alike — must call this rather than
    /// re-implementing the legality check, so the rules can't silently drift out of sync between
    /// callers. Throws <see cref="InvalidInvoiceStatusTransitionException"/> for any other
    /// transition, including a no-op "transition" to the current status.
    /// </summary>
    public void TransitionTo(InvoiceStatus target)
    {
        var isLegal = (Status, target) switch
        {
            (InvoiceStatus.Draft, InvoiceStatus.Unpaid) => true,
            (InvoiceStatus.Unpaid, InvoiceStatus.Overdue) => true,
            (InvoiceStatus.Unpaid, InvoiceStatus.Paid) => true,
            (InvoiceStatus.Overdue, InvoiceStatus.Paid) => true,
            (InvoiceStatus.Draft, InvoiceStatus.Cancelled) => true,
            (InvoiceStatus.Unpaid, InvoiceStatus.Cancelled) => true,
            _ => false
        };

        if (!isLegal)
        {
            throw new InvalidInvoiceStatusTransitionException(Status, target);
        }

        Status = target;
    }
}
