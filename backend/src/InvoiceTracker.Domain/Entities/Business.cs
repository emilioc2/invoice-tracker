namespace InvoiceTracker.Domain.Entities;

public class Business
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Nullable free-text default payment instructions (e.g. bank details, a payment link) shown
    // to clients on invoices unless overridden per-invoice — see architecture.md's "Business
    // payment details". Viewable/editable by any authenticated member of the business (Owner or
    // Staff), not just the Owner.
    public string? PaymentDetails { get; set; }
}
