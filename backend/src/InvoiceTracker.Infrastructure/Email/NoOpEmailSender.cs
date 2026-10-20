using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// TEMPORARY placeholder until build-order step 6 wires up the real Formspree-backed
/// <see cref="IEmailSender"/>. Doesn't actually send anything — just logs what would have been
/// sent, so forgot-password/reset-password can still be exercised locally (e.g. to read a reset
/// token out of the console) without a real email transport.
/// </summary>
public class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NoOpEmailSender] Would send to {To} | Subject: {Subject} | Body: {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
