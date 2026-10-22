namespace InvoiceTracker.Application.Common.Models;

/// <summary>
/// A single file to attach to an outbound email, passed to <see cref="InvoiceTracker.Application.Common.Interfaces.IEmailSender"/>.
/// Used today for the rendered invoice PDF on Send/Resend — see architecture.md's "Invoice
/// send/resend email delivery" section.
/// </summary>
public record EmailAttachment(string FileName, byte[] Content, string ContentType);
