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
    DateTime LastModifiedDate,
    DateTime? ArchivedAt,
    IReadOnlyList<InvoiceLineItemDto> LineItems);
