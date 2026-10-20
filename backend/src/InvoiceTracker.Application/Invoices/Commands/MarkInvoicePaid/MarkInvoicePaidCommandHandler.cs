using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.MarkInvoicePaid;

public class MarkInvoicePaidCommandHandler : IRequestHandler<MarkInvoicePaidCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public MarkInvoicePaidCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(MarkInvoicePaidCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.BusinessId == businessId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        if (invoice.Status is not (InvoiceStatus.Unpaid or InvoiceStatus.Overdue))
        {
            throw new BadRequestException("Only an Unpaid or Overdue invoice can be marked as Paid.");
        }

        invoice.Status = InvoiceStatus.Paid;
        invoice.LastModifiedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return invoice.ToDto();
    }
}
