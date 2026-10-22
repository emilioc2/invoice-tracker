namespace InvoiceTracker.Domain.Enums;

/// <summary>
/// What triggered a <see cref="InvoiceTracker.Domain.Entities.Notification"/> row:
/// <see cref="OverdueReminder"/> for the automatic Kafka-driven overdue pipeline, or
/// <see cref="InvoiceSent"/> for a manual, foreground Send/Resend-invoice-email action. See
/// architecture.md's "Invoice send/resend email delivery" section.
/// </summary>
public enum NotificationType
{
    OverdueReminder,
    InvoiceSent
}
