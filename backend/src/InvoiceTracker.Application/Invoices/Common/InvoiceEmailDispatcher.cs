using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Shared "insert Pending Notification -> attempt IEmailSender.SendAsync -> update to Sent/Failed"
/// sequence used identically by <c>SendInvoiceCommandHandler</c> (after the Draft -> Unpaid
/// transition commits) and <c>ResendInvoiceEmailCommandHandler</c> (with no preceding transition)
/// — factored out here so the two can never drift apart. Per CLAUDE.md's idempotency rule, the
/// Notification row is always written as <see cref="NotificationStatus.Pending"/> *before*
/// <see cref="IEmailSender.SendAsync"/> is ever called, and a thrown exception (not a returned
/// <c>false</c>, per this codebase's convention — see <c>OverdueNotificationConsumer</c>) is what
/// signals failure.
/// </summary>
public static class InvoiceEmailDispatcher
{
    public static async Task<EmailDeliveryResult> DispatchAsync(
        IApplicationDbContext dbContext,
        IEmailSender emailSender,
        ILogger logger,
        Invoice invoice,
        Client client,
        InvoicePdfGenerationResult pdf,
        CancellationToken cancellationToken)
    {
        var (subject, body) = InvoiceEmailContentBuilder.Build(invoice, client);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Status = NotificationStatus.Pending,
            NotificationType = NotificationType.InvoiceSent,
            Channel = "Email",
            Message = body,
            SourceEventId = Guid.NewGuid()
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        var attachment = new EmailAttachment($"{pdf.InvoiceNumber}.pdf", pdf.Content, "application/pdf");

        try
        {
            await emailSender.SendAsync(client.Email, subject, body, new[] { attachment }, cancellationToken);
            notification.Status = NotificationStatus.Sent;
            notification.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send invoice email for invoice {InvoiceId}.", invoice.Id);
            notification.Status = NotificationStatus.Failed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EmailDeliveryResult(notification.Status.ToString(), notification.Id);
    }
}
