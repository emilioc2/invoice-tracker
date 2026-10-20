using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Notifications.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Notifications.Queries.ListNotifications;

/// <summary>
/// Notification has no BusinessId column of its own (it only carries InvoiceId), so multi-tenancy
/// scoping is done by joining to Invoices and filtering on the caller's BusinessId there — same
/// rule as every other handler, just applied one hop away. If the caller passes an
/// <c>InvoiceId</c> belonging to a different business, it simply matches nothing here (the list
/// equivalent of the project's "cross-tenant access looks like it doesn't exist" rule — there's no
/// single record whose existence a 404-vs-200 response could leak).
/// </summary>
public class ListNotificationsQueryHandler : IRequestHandler<ListNotificationsQuery, PaginatedResult<NotificationDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ListNotificationsQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedResult<NotificationDto>> Handle(ListNotificationsQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var query =
            from notification in _dbContext.Notifications.AsNoTracking()
            join invoice in _dbContext.Invoices.AsNoTracking()
                on notification.InvoiceId equals invoice.Id
            where invoice.BusinessId == businessId
            select notification;

        if (request.InvoiceId.HasValue)
        {
            query = query.Where(n => n.InvoiceId == request.InvoiceId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Notification has no created/occurred timestamp of its own (see the entity) — SentAt is
        // only set once a send is attempted, so most-recently-sent-first, with Id as a stable
        // tie-break for rows that share a SentAt (or are both still null), is the closest
        // approximation of "most recent first" available from the existing shape.
        var entities = await query
            .OrderByDescending(n => n.SentAt)
            .ThenByDescending(n => n.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = entities.Select(n => n.ToDto()).ToList();

        return new PaginatedResult<NotificationDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
