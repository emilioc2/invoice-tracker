using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Server-side-only computation of an invoice's financial fields — shared by create and update
/// (while still Draft) so the two handlers can never drift. The client supplies line items
/// (Description/Quantity/UnitPrice) and TaxRate; everything here is derived, never accepted
/// directly from a request.
/// </summary>
public static class InvoiceTotalsCalculator
{
    public static List<InvoiceLineItem> BuildLineItems(IEnumerable<InvoiceLineItemRequest> requests) =>
        requests
            .Select(r => new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                Description = r.Description,
                Quantity = r.Quantity,
                UnitPrice = r.UnitPrice,
                LineTotal = r.Quantity * r.UnitPrice
            })
            .ToList();

    public static (decimal Subtotal, decimal TaxAmount, decimal Total) Calculate(
        IEnumerable<InvoiceLineItem> lineItems, decimal taxRate)
    {
        var subtotal = lineItems.Sum(li => li.LineTotal);
        var taxAmount = subtotal * taxRate;
        var total = subtotal + taxAmount;
        return (subtotal, taxAmount, total);
    }
}
