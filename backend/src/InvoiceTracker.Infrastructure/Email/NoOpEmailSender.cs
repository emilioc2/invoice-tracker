using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Registered as the <see cref="IEmailSender"/> fallback (see Infrastructure/DependencyInjection.cs)
/// whenever no real <c>Resend:ApiKey</c> is configured — including Development by default, since
/// appsettings.Development.json's Resend section is still the documented placeholder. Also used as
/// the basis for test registration: integration tests register this (or an NSubstitute fake)
/// instead, per this project's rule that tests must never make a real Resend call. Doesn't actually
/// send anything — just logs what would have been sent, which also makes it handy for locally
/// exercising forgot-password/reset-password (e.g. to read a reset token out of the console)
/// without a real email transport.
/// </summary>
public class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NoOpEmailSender] Would send to {To} | Subject: {Subject} | Body: {Body}", to, subject, body);

        foreach (var attachment in attachments)
        {
            _logger.LogInformation(
                "[NoOpEmailSender] Would attach {FileName} ({ByteCount} bytes)",
                attachment.FileName,
                attachment.Content.Length);
        }

        return Task.CompletedTask;
    }
}
