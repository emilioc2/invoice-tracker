using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Builds the subject/body for the invoice-copy email sent on Send and re-sent (identically) on
/// Resend — shared by <c>SendInvoiceCommandHandler</c> and <c>ResendInvoiceEmailCommandHandler</c>
/// so the two can never drift out of sync. Per requirements.md's "Invoice email delivery" section,
/// the body must carry the invoice number, amount, and due date (the PDF itself is attached
/// separately by the caller, not built here).
/// </summary>
public static class InvoiceEmailContentBuilder
{
    public static (string Subject, string Body) Build(Invoice invoice, Client client)
    {
        var subject = $"Invoice {invoice.InvoiceNumber}";

        var body =
            $"Dear {client.Name},\n\n" +
            $"Please find attached a copy of invoice {invoice.InvoiceNumber}.\n\n" +
            $"Amount due: {invoice.Total:F2}\n" +
            $"Due date: {invoice.DueDate:yyyy-MM-dd}\n\n" +
            "Thank you for your business.";

        return (subject, body);
    }
}
