using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Generic invoice-PDF-rendering port. The concrete adapter (QuestPDF) lives in Infrastructure —
/// the Application layer only depends on this shape.
/// </summary>
public interface IPdfGenerator
{
    Task<byte[]> GenerateInvoicePdfAsync(
        Invoice invoice,
        Client client,
        Business business,
        CancellationToken cancellationToken = default);
}
