using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Entities;

public class Invoice
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
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTime LastModifiedDate { get; set; }

    // null = active, non-null = archived. Never hard-deleted.
    public DateTime? ArchivedAt { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}
