using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.CancelInvoice;

public class CancelInvoiceCommandHandler : IRequestHandler<CancelInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public CancelInvoiceCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(CancelInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.BusinessId == businessId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        // Per the status workflow: only Draft/Unpaid can be cancelled — not Overdue, and
        // never once Paid.
        if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Unpaid))
        {
            throw new BadRequestException("Only a Draft or Unpaid invoice can be cancelled.");
        }

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.LastModifiedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return invoice.ToDto();
    }
}
