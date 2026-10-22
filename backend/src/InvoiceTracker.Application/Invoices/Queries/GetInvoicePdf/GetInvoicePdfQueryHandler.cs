using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;

/// <summary>
/// Loads an invoice (incl. line items), its client, and the owning business in a single joined
/// query/round trip — all scoped by the caller's BusinessId, same multi-tenancy/404 rule as
/// GetInvoiceByIdQueryHandler — then renders them to a PDF via IPdfGenerator. Works regardless of
/// Status (Draft included); there's no rule in requirements.md restricting PDF export to sent
/// invoices.
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
            where invoice.Id == request.Id
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

        return new InvoicePdfResult(pdfBytes, $"{result.invoice.InvoiceNumber}.pdf");
    }
}
