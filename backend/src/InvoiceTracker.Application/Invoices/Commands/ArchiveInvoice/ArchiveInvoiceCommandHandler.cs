using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.ArchiveInvoice;

/// <summary>Soft delete: sets ArchivedAt. Idempotent — archiving an already-archived invoice is a no-op.</summary>
public class ArchiveInvoiceCommandHandler : IRequestHandler<ArchiveInvoiceCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ArchiveInvoiceCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(ArchiveInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.BusinessId == businessId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        invoice.ArchivedAt ??= DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
