using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Invoices.Common;

public static class InvoiceMappingExtensions
{
    public static InvoiceDto ToDto(this Invoice invoice, string? clientName = null) =>
        new(
            invoice.Id,
            invoice.ClientId,
            invoice.InvoiceNumber,
            invoice.CreatedAtDate,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.Status.ToString(),
            invoice.Subtotal,
            invoice.TaxRate,
            invoice.TaxAmount,
            invoice.Total,
            invoice.Notes,
            invoice.LastModifiedDate,
            invoice.ArchivedAt,
            invoice.LineItems.Select(li => li.ToDto()).ToList(),
            clientName);

    public static InvoiceLineItemDto ToDto(this InvoiceLineItem lineItem) =>
        new(lineItem.Id, lineItem.Description, lineItem.Quantity, lineItem.UnitPrice, lineItem.LineTotal);
}
