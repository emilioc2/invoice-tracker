using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoiceById;

/// <summary>Returns an invoice (incl. line items) regardless of ArchivedAt — same rule as clients.</summary>
public class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetInvoiceByIdQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.BusinessId == businessId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        return invoice.ToDto();
    }
}
