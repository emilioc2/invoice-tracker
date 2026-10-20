namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Client-supplied shape for one line item on create/update. <c>LineTotal</c> is deliberately not
/// part of this type — the handler always computes it server-side as
/// <c>Quantity * UnitPrice</c>, never trusting a client-sent value.
/// </summary>
public record InvoiceLineItemRequest(string Description, decimal Quantity, decimal UnitPrice);
