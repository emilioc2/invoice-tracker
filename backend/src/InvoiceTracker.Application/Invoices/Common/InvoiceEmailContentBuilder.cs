using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Builds the subject/body for the two letter-format emails this project sends about an invoice:
/// <see cref="Build"/> for the invoice-copy email sent on Send and re-sent (identically) on Resend
/// — shared by <c>SendInvoiceCommandHandler</c> and <c>ResendInvoiceEmailCommandHandler</c> so the
/// two can never drift out of sync — and <see cref="BuildOverdueReminder"/> for the automatic,
/// Kafka-driven overdue reminder sent by <c>OverdueNotificationConsumer</c>. Per requirements.md's
/// "Invoice email delivery"/"Overdue reminder email content" sections, both bodies carry the
/// invoice number, amount, and due date and follow the same greeting/closing conventions — only the
/// framing differs ("here's your invoice" vs. "payment is late") — so keeping both methods in this
/// one file means a future wording change to one can't silently drift from the other. The PDF
/// itself is attached separately by each caller, not built here.
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

    /// <summary>
    /// Builds the subject/body for the automatic, Kafka-driven overdue reminder email sent by
    /// <c>OverdueNotificationConsumer</c>. Deliberately structured the same way as <see cref="Build"/>
    /// above — greeting, amount due, due date, closing — but worded for "payment is late" rather
    /// than "here's a copy of your invoice", per requirements.md's "Overdue reminder email content"
    /// section. Kept right alongside <see cref="Build"/> in this same file so a future wording
    /// change to one template can't silently drift from the other.
    ///
    /// Takes primitive fields (invoice number, client name, total, due date) rather than
    /// <c>Invoice</c>/<c>Client</c> entities like <see cref="Build"/> does: the consumer already has
    /// all of these on the <c>InvoiceOverdueEvent</c> it's processing (the event is deliberately a
    /// lightweight invoice/client summary, per architecture.md), so there is no need for it to issue
    /// a second database query just to re-fetch the same values as domain entities before calling
    /// this builder. The invoice PDF attachment is still sourced from the database, via
    /// <c>IInvoicePdfService.GenerateAsync</c>, by the consumer itself.
    /// </summary>
    public static (string Subject, string Body) BuildOverdueReminder(
        string invoiceNumber, string clientName, decimal totalAmount, DateTime dueDate)
    {
        var subject = $"Invoice {invoiceNumber} is overdue";

        var body =
            $"Dear {clientName},\n\n" +
            $"This is a reminder that invoice {invoiceNumber} is now overdue.\n\n" +
            $"Amount due: {totalAmount:F2}\n" +
            $"Due date: {dueDate:yyyy-MM-dd}\n\n" +
            "Please arrange payment as soon as possible. A copy of the invoice is attached for your reference.\n\n" +
            "Thank you.";

        return (subject, body);
    }
}
