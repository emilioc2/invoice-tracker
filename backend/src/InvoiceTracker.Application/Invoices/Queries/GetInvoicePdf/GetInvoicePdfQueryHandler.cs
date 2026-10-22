using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Invoices.Common;
using MediatR;

namespace InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;

/// <summary>
/// Delegates the Invoice+LineItems+Client+Business assembly query and PDF rendering to the shared
/// <see cref="IInvoicePdfService"/> (also used by Send/Resend-invoice-email) — this handler's own
/// job is just to adapt that result to the <see cref="InvoicePdfResult"/> shape this endpoint
/// returns. Works regardless of Status (Draft included); there's no rule in requirements.md
/// restricting PDF export to sent invoices.
/// </summary>
public class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, InvoicePdfResult>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IInvoicePdfService _invoicePdfService;

    public GetInvoicePdfQueryHandler(ICurrentUserService currentUserService, IInvoicePdfService invoicePdfService)
    {
        _currentUserService = currentUserService;
        _invoicePdfService = invoicePdfService;
    }

    public async Task<InvoicePdfResult> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var result = await _invoicePdfService.GenerateAsync(request.Id, businessId, cancellationToken);

        return new InvoicePdfResult(result.Content, $"{result.InvoiceNumber}.pdf");
    }
}
