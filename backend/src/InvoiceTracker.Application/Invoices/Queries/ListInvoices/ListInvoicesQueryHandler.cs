using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Queries.ListInvoices;

/// <summary>Default list only shows active (non-archived) invoices for the caller's business.</summary>
public class ListInvoicesQueryHandler : IRequestHandler<ListInvoicesQuery, PaginatedResult<InvoiceDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ListInvoicesQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedResult<InvoiceDto>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var query = _dbContext.Invoices
            .Include(i => i.LineItems)
            .AsNoTracking()
            .Where(i => i.BusinessId == businessId && i.ArchivedAt == null);

        if (request.Status.HasValue)
        {
            query = query.Where(i => i.Status == request.Status.Value);
        }

        if (request.ClientId.HasValue)
        {
            query = query.Where(i => i.ClientId == request.ClientId.Value);
        }

        if (request.DateFrom.HasValue)
        {
            var dateFrom = NormalizeToUtc(request.DateFrom.Value);
            query = query.Where(i => i.DueDate >= dateFrom);
        }

        if (request.DateTo.HasValue)
        {
            var dateTo = NormalizeToUtc(request.DateTo.Value);
            query = query.Where(i => i.DueDate <= dateTo);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var entities = await query
            .OrderByDescending(i => i.CreatedAtDate)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = entities.Select(i => i.ToDto()).ToList();

        return new PaginatedResult<InvoiceDto>(items, totalCount, request.PageNumber, request.PageSize);
    }

    /// <summary>
    /// [FromQuery] DateTime? binding (ASP.NET Core's own DateTime.TryParse-based model binder, not
    /// System.Text.Json) produces Kind = Unspecified for a bare date-only query value (e.g.
    /// ?dateFrom=2026-01-01) — exactly what an API consumer might reasonably send. Npgsql (v6+)
    /// rejects parameterizing a Kind = Unspecified DateTime against a timestamptz column, so this
    /// normalizes right before the value is used in an EF Core predicate, the actual point of
    /// contact with Npgsql for this binding path.
    /// </summary>
    private static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
