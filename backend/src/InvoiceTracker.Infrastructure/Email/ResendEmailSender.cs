using System.Net.Http.Headers;
using System.Net.Http.Json;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Posts a single JSON request to Resend's email API (https://api.resend.com/emails) —
/// attachments travel as base64-encoded content in the same payload as the rest of the message,
/// simpler than a multipart upload.
///
/// Known limitation (documented in requirements.md/README): this uses Resend's shared default
/// sender address (<c>onboarding@resend.dev</c>, no domain/DNS verification), which in sandbox mode
/// can only deliver to the email address that owns the Resend account rather than to arbitrary
/// third-party recipients — this is for testing purposes, so it proves the outbound integration
/// (including attachment delivery) fires correctly on events, rather than guaranteeing delivery to
/// each client's actual inbox. That's the documented trade-off, not a bug to work around here.
///
/// Registered via <c>AddHttpClient&lt;IEmailSender, ResendEmailSender&gt;()</c> in
/// Infrastructure/DependencyInjection.cs whenever a real <c>Resend:ApiKey</c> is configured (any
/// environment, including Development) — <see cref="NoOpEmailSender"/> is the fallback otherwise.
/// Integration tests also register a fake IEmailSender and must never make a real Resend call.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private const string EmailsPath = "emails";

    private readonly HttpClient _httpClient;
    private readonly ResendSettings _settings;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendSettings> settings, ILogger<ResendEmailSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
    }

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            from = _settings.FromAddress,
            to = new[] { to },
            subject,
            text = body,
            attachments = attachments.Count == 0
                ? null
                : attachments
                    .Select(attachment => new
                    {
                        filename = attachment.FileName,
                        content = Convert.ToBase64String(attachment.Content),
                        content_type = attachment.ContentType
                    })
                    .ToArray()
        };

        using var response = await _httpClient.PostAsJsonAsync(EmailsPath, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Resend request failed with status {(int)response.StatusCode}: {responseBody}");
        }

        _logger.LogInformation(
            "[ResendEmailSender] Sent to {To} | Subject: {Subject} | Attachments: {AttachmentCount}",
            to, subject, attachments.Count);
    }
}
