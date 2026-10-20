namespace InvoiceTracker.Application.Invoices.Common;

public record InvoiceLineItemDto(Guid Id, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);
