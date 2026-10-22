using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;
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
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Invoice not found.", cancellationToken);

        try
        {
            invoice.TransitionTo(InvoiceStatus.Unpaid);
        }
        catch (InvalidInvoiceStatusTransitionException)
        {
            throw new BadRequestException("Only Draft invoices can be sent.");
        }

        var now = DateTime.UtcNow;
        invoice.IssueDate = now;
        invoice.LastModifiedDate = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return invoice.ToDto();
    }
}
