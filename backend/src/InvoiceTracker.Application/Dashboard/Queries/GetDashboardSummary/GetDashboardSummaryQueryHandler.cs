using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Dashboard.Queries.GetDashboardSummary;

/// <summary>
/// Single grouped query against Invoices — one round trip gets both the per-status counts and the
/// per-status sum of Total, rather than running a separate query per status. See
/// <see cref="DashboardSummaryDto"/> for the "outstanding" status set and archived-invoice
/// exclusion reasoning.
/// </summary>
public class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    private static readonly InvoiceStatus[] OutstandingStatuses = { InvoiceStatus.Unpaid, InvoiceStatus.Overdue };

    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetDashboardSummaryQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var grouped = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.BusinessId == businessId && i.ArchivedAt == null)
            .GroupBy(i => i.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                TotalSum = g.Sum(i => i.Total)
            })
            .ToListAsync(cancellationToken);

        // Zero-fill every InvoiceStatus value so the frontend always gets all 5 buckets, even
        // ones with no invoices for this business yet.
        var countsByStatus = Enum.GetValues<InvoiceStatus>()
            .Select(status =>
            {
                var match = grouped.FirstOrDefault(g => g.Status == status);
                return new StatusCountDto(status.ToString(), match?.Count ?? 0);
            })
            .ToList();

        var overdueCount = grouped.FirstOrDefault(g => g.Status == InvoiceStatus.Overdue)?.Count ?? 0;

        var outstandingTotal = grouped
            .Where(g => OutstandingStatuses.Contains(g.Status))
            .Sum(g => g.TotalSum);

        return new DashboardSummaryDto(outstandingTotal, overdueCount, countsByStatus);
    }
}
