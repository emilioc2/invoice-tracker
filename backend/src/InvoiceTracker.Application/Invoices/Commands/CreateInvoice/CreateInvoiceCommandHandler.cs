using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Commands.CreateInvoice;

public class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, InvoiceDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public CreateInvoiceCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<InvoiceDto> Handle(CreateInvoiceCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        // The client belonging to a different business is indistinguishable from one that
        // doesn't exist — 404, not 403, per the multi-tenancy rule.
        var clientExists = await _dbContext.Clients
            .AnyAsync(c => c.Id == request.ClientId && c.BusinessId == businessId, cancellationToken);
        if (!clientExists)
        {
            throw new NotFoundException("Client not found.");
        }

        var lineItems = InvoiceTotalsCalculator.BuildLineItems(request.LineItems);
        var (subtotal, taxAmount, total) = InvoiceTotalsCalculator.Calculate(lineItems, request.TaxRate);

        // Numbering (GetNextInvoiceNumberAsync) and the invoice insert below must commit together
        // — an explicit transaction is what makes that atomic.
        await using var transaction = await _dbContext.BeginTransactionAsync(cancellationToken);

        var nextNumber = await _dbContext.GetNextInvoiceNumberAsync(businessId, cancellationToken);
        var invoiceNumber = $"INV-{nextNumber:D4}";

        var now = DateTime.UtcNow;
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ClientId = request.ClientId,
            InvoiceNumber = invoiceNumber,
            CreatedAtDate = now,
            IssueDate = null,
            DueDate = request.DueDate,
            Status = InvoiceStatus.Draft,
            Subtotal = subtotal,
            TaxRate = request.TaxRate,
            TaxAmount = taxAmount,
            Total = total,
            Notes = request.Notes,
            LastModifiedDate = now,
            LineItems = lineItems
        };

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return invoice.ToDto();
    }
}
