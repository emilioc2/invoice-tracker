using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Not used as the production <see cref="IEmailSender"/> — <see cref="FormspreeEmailSender"/> is
/// registered for that (see Infrastructure/DependencyInjection.cs). Kept in the codebase for test
/// registration: integration tests register this (or an NSubstitute fake) instead, per this
/// project's rule that tests must never make a real Formspree call. Doesn't actually send
/// anything — just logs what would have been sent, which also makes it handy for locally
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

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[NoOpEmailSender] Would send to {To} | Subject: {Subject} | Body: {Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
