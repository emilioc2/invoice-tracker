using InvoiceTracker.Application.Common.Models;

namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Generic outbound email port. The concrete adapter (Formspree, or a no-op placeholder
/// until that's wired up) lives in Infrastructure — the Application layer only depends on this shape.
///
/// <paramref name="attachments"/> is required (not optional/defaulted) deliberately: this is a
/// breaking change to an existing interface, and every call site should have to make an explicit
/// choice (<c>Array.Empty&lt;EmailAttachment&gt;()</c> for "no attachment") rather than silently
/// inheriting a default. Per this project's established convention (see
/// <c>OverdueNotificationConsumer</c>), a failed send is signalled by throwing — never by
/// returning <c>false</c> — so callers wrap this in try/catch to decide Sent vs Failed.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default);
}
