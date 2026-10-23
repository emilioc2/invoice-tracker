namespace InvoiceTracker.Application.Invoices.Common;

public record InvoiceDto(
    Guid Id,
    Guid ClientId,
    string InvoiceNumber,
    DateTime CreatedAtDate,
    DateTime? IssueDate,
    DateTime DueDate,
    string Status,
    decimal Subtotal,
    decimal TaxRate,
    decimal TaxAmount,
    decimal Total,
    string? Notes,
    string? PaymentDetails,
    DateTime LastModifiedDate,
    DateTime? ArchivedAt,
    IReadOnlyList<InvoiceLineItemDto> LineItems,
    // Denormalized display field for list views (e.g. GET /api/invoices) so the frontend doesn't
    // need a separate per-row fetch of the client just to show its name. Populated by handlers that
    // join against Clients (see ListInvoicesQueryHandler); null/omitted elsewhere.
    string? ClientName = null);
