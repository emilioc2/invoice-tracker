namespace InvoiceTracker.Application.Invoices.Common;

/// <summary>Rendered invoice PDF bytes, plus the filename the controller should serve it under.</summary>
public record InvoicePdfResult(byte[] Content, string FileName);
