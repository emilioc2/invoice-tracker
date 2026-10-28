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

        // Join against Invoices (scoped to the same business) in the same query to pick up the
        // invoice's display number for each row, rather than making the frontend fetch each invoice
        // separately per row — see architecture.md's "List DTO shape" note.
        var query =
            from notification in _dbContext.Notifications.AsNoTracking()
            join invoice in _dbContext.Invoices.AsNoTracking()
                on notification.InvoiceId equals invoice.Id
            where invoice.BusinessId == businessId
            select new { Notification = notification, invoice.InvoiceNumber };

        if (request.InvoiceId.HasValue)
        {
            query = query.Where(x => x.Notification.InvoiceId == request.InvoiceId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Notification has no created/occurred timestamp of its own (see the entity) — SentAt is
        // only set once a send is attempted, so most-recently-sent-first, with Id as a stable
        // tie-break for rows that share a SentAt (or are both still null), is the closest
        // approximation of "most recent first" available from the existing shape.
        //
        // The leading OrderBy(x => x.Notification.SentAt == null) sorts false (0) before true (1),
        // i.e. rows with a real SentAt before null-SentAt rows. This is deliberate and
        // PostgreSQL-specific: ORDER BY ... DESC on a nullable column defaults to NULLS FIRST, so a
        // plain OrderByDescending(SentAt) would put every still-Pending/Failed row (SentAt = null)
        // ahead of every successfully-sent row regardless of how long ago it was sent. Without this
        // null-last guard first, the Notifications page would make recent successful sends look
        // missing, pushed down by unrelated failed/pending rows.
        var page = await query
            .OrderBy(x => x.Notification.SentAt == null)
            .ThenByDescending(x => x.Notification.SentAt)
            .ThenByDescending(x => x.Notification.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = page.Select(x => x.Notification.ToDto(x.InvoiceNumber)).ToList();

        return new PaginatedResult<NotificationDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
