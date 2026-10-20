using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public DateTime? SentAt { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    // Unique identifier of the Kafka event that triggered this notification.
    // Checked before processing to make consumption idempotent/crash-safe.
    public Guid SourceEventId { get; set; }
}
