using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;
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
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Invoice not found.", cancellationToken);

        try
        {
            invoice.TransitionTo(InvoiceStatus.Paid);
        }
        catch (InvalidInvoiceStatusTransitionException)
        {
            throw new BadRequestException("Only an Unpaid or Overdue invoice can be marked as Paid.");
        }

        invoice.LastModifiedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return invoice.ToDto();
    }
}
