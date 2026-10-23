using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.UpdateInvoice;

public class UpdateInvoiceCommandHandler : IRequestHandler<UpdateInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public UpdateInvoiceCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .GetForBusinessOrThrowAsync(request.Id, businessId, "Invoice not found.", cancellationToken);

        // Edit lock (not a status transition — Status never changes here), per CLAUDE.md's
        // "Invoice edit lock" convention: a Draft invoice is the only one still editable.
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new BadRequestException("Only Draft invoices can be edited. Cancel and recreate it instead.");
        }

        var clientExists = await _dbContext.Clients
            .AnyAsync(c => c.Id == request.ClientId && c.BusinessId == businessId, cancellationToken);
        if (!clientExists)
        {
            throw new NotFoundException("Client not found.");
        }

        // Replace the existing line items wholesale rather than diffing — simplest correct
        // approach for a small per-invoice list. Both the removal and the addition are done via
        // explicit DbSet calls (not by just reassigning invoice.LineItems): these new
        // InvoiceLineItem instances carry client-generated (non-default) Guid keys, and an entity
        // with a pre-set key that's only reached via navigation fixup — never explicitly Add()'d —
        // gets misclassified by EF Core's change tracker as an existing/Modified row rather than a
        // new one, which blows up at SaveChanges with a "0 rows affected" concurrency exception.
        _dbContext.InvoiceLineItems.RemoveRange(invoice.LineItems);

        var lineItems = InvoiceTotalsCalculator.BuildLineItems(request.LineItems);
        foreach (var lineItem in lineItems)
        {
            lineItem.InvoiceId = invoice.Id;
        }

        _dbContext.InvoiceLineItems.AddRange(lineItems);

        var (subtotal, taxAmount, total) = InvoiceTotalsCalculator.Calculate(lineItems, request.TaxRate);

        invoice.ClientId = request.ClientId;
        invoice.DueDate = request.DueDate;
        invoice.TaxRate = request.TaxRate;
        invoice.Notes = request.Notes;
        invoice.PaymentDetails = request.PaymentDetails;
        invoice.Subtotal = subtotal;
        invoice.TaxAmount = taxAmount;
        invoice.Total = total;
        invoice.LastModifiedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Reflects the new set for the returned DTO — safe to assign after SaveChanges since
        // these instances are already tracked (Added, now persisted) by the explicit AddRange above.
        invoice.LineItems = lineItems;

        return invoice.ToDto();
    }
}
