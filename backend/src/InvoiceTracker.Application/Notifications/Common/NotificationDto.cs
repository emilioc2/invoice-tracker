namespace InvoiceTracker.Application.Notifications.Common;

public record NotificationDto(
    Guid Id,
    Guid InvoiceId,
    string Status,
    DateTime? SentAt,
    string Channel,
    string Message,
    Guid SourceEventId);
