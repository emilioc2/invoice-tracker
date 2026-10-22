using InvoiceTracker.Application.Common.Models;

namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Shared invoice-PDF-assembly port used by every handler that needs the rendered PDF for a given
/// invoice: <c>GetInvoicePdfQueryHandler</c> (PDF download), <c>SendInvoiceCommandHandler</c> and
/// <c>ResendInvoiceEmailCommandHandler</c> (PDF email attachment). Implemented in the Application
/// layer itself (<c>Application/Common/Services/InvoicePdfService</c>), not Infrastructure — it
/// only orchestrates <see cref="IApplicationDbContext"/> (for the Invoice+LineItems+Client+Business
/// assembly query) and <see cref="IPdfGenerator"/> (for rendering), both of which are already
/// Application-layer-visible dependencies.
///
/// Returns <see cref="InvoicePdfGenerationResult"/> (bytes + invoice number) rather than bare
/// <c>byte[]</c> — see that record's doc comment for why.
/// </summary>
public interface IInvoicePdfService
{
    Task<InvoicePdfGenerationResult> GenerateAsync(Guid invoiceId, Guid businessId, CancellationToken cancellationToken);
}
