namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>
/// Outcome of a single Send/Resend-invoice-email attempt, reported back to the caller separately
/// from the (always-independent) Send/Resend action's own result — see architecture.md's "Invoice
/// send/resend email delivery" section. <paramref name="Status"/> is <c>"Sent"</c> or
/// <c>"Failed"</c> (the string form of <see cref="InvoiceTracker.Domain.Enums.NotificationStatus"/>,
/// mirroring how every other DTO in this codebase surfaces an enum).
/// </summary>
public record EmailDeliveryResult(string Status, Guid NotificationId);
