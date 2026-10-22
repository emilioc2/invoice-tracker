namespace InvoiceTracker.Application.Notifications.Common;

public record NotificationDto(
    Guid Id,
    Guid InvoiceId,
    string Status,
    string NotificationType,
    DateTime? SentAt,
    string Channel,
    string Message,
    Guid SourceEventId,
    // Denormalized display field for list views (e.g. GET /api/notifications) so the frontend
    // doesn't need a separate per-row fetch of the invoice just to show its number. Populated by
    // handlers that join against Invoices (see ListNotificationsQueryHandler); null/omitted
    // elsewhere.
    string? InvoiceNumber = null);
