using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Notifications.Common;
using MediatR;

namespace InvoiceTracker.Application.Notifications.Queries.ListNotifications;

public record ListNotificationsQuery(
    Guid? InvoiceId,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedResult<NotificationDto>>;
