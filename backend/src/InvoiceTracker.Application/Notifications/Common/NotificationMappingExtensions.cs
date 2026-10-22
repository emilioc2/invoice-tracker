using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Notifications.Common;

public static class NotificationMappingExtensions
{
    public static NotificationDto ToDto(this Notification notification, string? invoiceNumber = null) =>
        new(
            notification.Id,
            notification.InvoiceId,
            notification.Status.ToString(),
            notification.SentAt,
            notification.Channel,
            notification.Message,
            notification.SourceEventId,
            invoiceNumber);
}
