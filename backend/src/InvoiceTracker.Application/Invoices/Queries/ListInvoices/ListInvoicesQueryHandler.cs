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
            query = query.Where(i => i.DueDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(i => i.DueDate <= request.DateTo.Value);
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
}
