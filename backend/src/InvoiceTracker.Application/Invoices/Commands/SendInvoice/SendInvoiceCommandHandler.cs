using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.SendInvoice;

public class SendInvoiceCommandHandler : IRequestHandler<SendInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public SendInvoiceCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.BusinessId == businessId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new BadRequestException("Only Draft invoices can be sent.");
        }

        var now = DateTime.UtcNow;
        invoice.IssueDate = now;
        invoice.Status = InvoiceStatus.Unpaid;
        invoice.LastModifiedDate = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return invoice.ToDto();
    }
}
