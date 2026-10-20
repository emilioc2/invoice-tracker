namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Generic outbound email port. The concrete adapter (Formspree, or a no-op placeholder
/// until that's wired up) lives in Infrastructure — the Application layer only depends on this shape.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
