using System.Collections.Concurrent;
using InvoiceTracker.Application.Common.Interfaces;

namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Fake <see cref="IEmailSender"/> registered for the entire integration test suite in place of
/// the production <c>FormspreeEmailSender</c> — per architecture.md's explicit instruction, tests
/// must never make a real Formspree HTTP call. Unlike the codebase's own
/// <c>NoOpEmailSender</c> (which only logs), this one records every call so tests can assert on
/// what was "sent" — e.g. pulling the password-reset token out of the forgot-password email body.
/// Registered as a singleton (one instance for the whole test run, shared across requests), so the
/// capture list is thread-safe.
/// </summary>
public class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sentEmails = new();

    public IReadOnlyCollection<SentEmail> SentEmails => _sentEmails.ToArray();

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sentEmails.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }

    /// <summary>Finds the most recently sent email to <paramref name="to"/>, if any.</summary>
    public SentEmail? FindLatestTo(string to)
    {
        return _sentEmails
            .Where(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
    }
}

public record SentEmail(string To, string Subject, string Body);
