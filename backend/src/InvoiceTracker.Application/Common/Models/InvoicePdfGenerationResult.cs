namespace InvoiceTracker.Application.Common.Models;

/// <summary>
/// Result of <see cref="InvoiceTracker.Application.Common.Interfaces.IInvoicePdfService.GenerateAsync"/>:
/// the rendered PDF bytes, plus the invoice's display number. Bundling the invoice number here
/// (rather than returning bare <c>byte[]</c>, as architecture.md's draft snippet shows) is a
/// deliberate, documented deviation — <c>GetInvoicePdfQueryHandler</c> needs the invoice number to
/// build its existing <c>InvoicePdfResult(pdfBytes, "{invoiceNumber}.pdf")</c> filename, and the
/// service's own joined query already has it in hand, so returning it here avoids a second,
/// otherwise-redundant database round trip in that handler just to re-fetch the same number.
/// </summary>
public record InvoicePdfGenerationResult(byte[] Content, string InvoiceNumber);
