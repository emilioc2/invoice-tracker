using MediatR;

namespace InvoiceTracker.Application.Dashboard.Queries.GetDashboardSummary;

/// <summary>
/// No parameters beyond the implicit BusinessId resolved server-side from the JWT — there's
/// nothing a client could supply here, so there's no matching FluentValidation validator either.
/// </summary>
public record GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>;
