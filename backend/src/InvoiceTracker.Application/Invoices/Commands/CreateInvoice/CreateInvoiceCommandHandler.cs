using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Entities;
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

        // An explicit override on the request wins outright. Otherwise, snapshot the business's
        // *current* PaymentDetails value now — a one-time copy, not a live reference, per
        // architecture.md's "Business payment details": a later change to Business.PaymentDetails
        // must never retroactively touch this invoice.
        var paymentDetails = request.PaymentDetails;
        if (paymentDetails is null)
        {
            paymentDetails = await _dbContext.Businesses
                .Where(b => b.Id == businessId)
                .Select(b => b.PaymentDetails)
                .SingleAsync(cancellationToken);
        }

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
            // Status isn't set here — Invoice.Status's setter is private (see Invoice.cs); every
            // new invoice simply starts at its default, Draft.
            Subtotal = subtotal,
            TaxRate = request.TaxRate,
            TaxAmount = taxAmount,
            Total = total,
            Notes = request.Notes,
            PaymentDetails = paymentDetails,
            LastModifiedDate = now,
            LineItems = lineItems
        };

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return invoice.ToDto();
    }
}
