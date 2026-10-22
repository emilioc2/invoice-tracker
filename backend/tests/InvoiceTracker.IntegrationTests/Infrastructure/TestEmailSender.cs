using System.Collections.Concurrent;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;

namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Fake <see cref="IEmailSender"/> registered for the entire integration test suite in place of
/// the production <c>FormspreeEmailSender</c> — per architecture.md's explicit instruction, tests
/// must never make a real Formspree HTTP call. Unlike the codebase's own
/// <c>NoOpEmailSender</c> (which only logs), this one records every call so tests can assert on
/// what was "sent" — e.g. pulling the password-reset token out of the forgot-password email body.
/// Registered as a singleton (one instance for the whole test run, shared across requests), so the
/// capture list is thread-safe.
///
/// <see cref="ShouldFail"/> lets a test force every subsequent <see cref="SendAsync"/> call to
/// throw (mirroring the real <c>FormspreeEmailSender</c>'s exception-based failure signal), so the
/// Send/Resend-email integration tests can prove the invoice status transition and the
/// Notification row's outcome are correct even when email delivery fails.
/// </summary>
public class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sentEmails = new();

    public IReadOnlyCollection<SentEmail> SentEmails => _sentEmails.ToArray();

    /// <summary>When true, every call to <see cref="SendAsync"/> throws instead of recording.</summary>
    public bool ShouldFail { get; set; }

    public Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("TestEmailSender configured to fail.");
        }

        _sentEmails.Enqueue(new SentEmail(to, subject, body, attachments));
        return Task.CompletedTask;
    }

    /// <summary>Finds the most recently sent email to <paramref name="to"/>, if any.</summary>
    public SentEmail? FindLatestTo(string? to)
    {
        return _sentEmails
            .Where(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
    }
}

public record SentEmail(string To, string Subject, string Body, IReadOnlyList<EmailAttachment> Attachments);
