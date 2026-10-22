using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Common.Services;

/// <summary>
/// Default <see cref="IInvoicePdfService"/> implementation. Extracted from
/// <c>GetInvoicePdfQueryHandler</c>'s original inline logic so it can be shared with
/// <c>SendInvoiceCommandHandler</c>/<c>ResendInvoiceEmailCommandHandler</c> (both need the same
/// rendered PDF, as an email attachment rather than an HTTP download) without each handler
/// re-assembling the same joined query.
/// </summary>
public class InvoicePdfService : IInvoicePdfService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPdfGenerator _pdfGenerator;

    public InvoicePdfService(IApplicationDbContext dbContext, IPdfGenerator pdfGenerator)
    {
        _dbContext = dbContext;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<InvoicePdfGenerationResult> GenerateAsync(Guid invoiceId, Guid businessId, CancellationToken cancellationToken)
    {
        // Single joined query (one DbContext round trip) for everything the PDF template needs:
        // the invoice (with line items via Include), its client, and the owning business. The
        // business lookup has no dependency on the invoice row, but there's no benefit to fetching
        // it separately — joining it in up front still costs only one round trip, scoped by the
        // same businessId throughout so a cross-tenant invoice id (or a client/business row that
        // doesn't belong to this business) can never be picked up.
        var result = await (
            from invoice in _dbContext.Invoices.Include(i => i.LineItems)
            join client in _dbContext.Clients on invoice.ClientId equals client.Id
            join business in _dbContext.Businesses on invoice.BusinessId equals business.Id
            where invoice.Id == invoiceId
                && invoice.BusinessId == businessId
                && client.BusinessId == businessId
            select new { invoice, client, business })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            throw new NotFoundException("Invoice not found.");
        }

        var pdfBytes = await _pdfGenerator.GenerateInvoicePdfAsync(
            result.invoice, result.client, result.business, cancellationToken);

        return new InvoicePdfGenerationResult(pdfBytes, result.invoice.InvoiceNumber);
    }
}
