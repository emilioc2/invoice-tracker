using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;

/// <summary>
/// Loads an invoice (incl. line items), its client, and the owning business — all scoped by the
/// caller's BusinessId, same multi-tenancy/404 rule as GetInvoiceByIdQueryHandler — then renders
/// them to a PDF via IPdfGenerator. Works regardless of Status (Draft included); there's no rule
/// in requirements.md restricting PDF export to sent invoices.
/// </summary>
public class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, InvoicePdfResult>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPdfGenerator _pdfGenerator;

    public GetInvoicePdfQueryHandler(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IPdfGenerator pdfGenerator)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<InvoicePdfResult> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
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

        // Scoped by the same businessId as the invoice above, so a cross-tenant client row can
        // never be picked up even if ClientId somehow pointed at one.
        var client = await _dbContext.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == invoice.ClientId && c.BusinessId == businessId, cancellationToken);

        if (client is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        var business = await _dbContext.Businesses
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == businessId, cancellationToken);

        if (business is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        var pdfBytes = await _pdfGenerator.GenerateInvoicePdfAsync(invoice, client, business, cancellationToken);

        return new InvoicePdfResult(pdfBytes, $"{invoice.InvoiceNumber}.pdf");
    }
}
