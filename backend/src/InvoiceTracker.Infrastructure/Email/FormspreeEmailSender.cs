using System.Net.Http.Json;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Posts the notification to a configured Formspree form endpoint. Known limitation (documented in
/// requirements.md/README): Formspree forwards everything to the form's own configured inbox
/// rather than to arbitrary third-party recipients, so this adapter proves the outbound
/// integration actually fires on events (Kafka consumer -> here -> HTTP call out) rather than
/// guaranteeing delivery to each client's real inbox — that's the documented trade-off, not a bug
/// to work around here.
///
/// Registered via <c>AddHttpClient&lt;IEmailSender, FormspreeEmailSender&gt;()</c> in
/// Infrastructure/DependencyInjection.cs for every environment except Development, which uses
/// <see cref="NoOpEmailSender"/> instead (Development's Formspree:FormEndpoint is still the
/// documented placeholder, so a real call here would 404 and throw). Integration tests also
/// register a fake IEmailSender and must never make a real Formspree call.
/// </summary>
public class FormspreeEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly FormspreeSettings _settings;
    private readonly ILogger<FormspreeEmailSender> _logger;

    public FormspreeEmailSender(HttpClient httpClient, IOptions<FormspreeSettings> settings, ILogger<FormspreeEmailSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        using var response = attachments.Count == 0
            ? await SendWithoutAttachmentsAsync(to, subject, body, cancellationToken)
            : await SendWithAttachmentsAsync(to, subject, body, attachments, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Formspree request to {_settings.FormEndpoint} failed with status {(int)response.StatusCode}: {responseBody}");
        }

        _logger.LogInformation(
            "[FormspreeEmailSender] Sent to {To} | Subject: {Subject} | Attachments: {AttachmentCount}",
            to, subject, attachments.Count);
    }

    /// <summary>
    /// Plain JSON POST — unchanged from before attachments existed. Kept as the no-attachment path
    /// (rather than always going through multipart) purely to avoid touching the request shape for
    /// the far more common case (every overdue-reminder and forgot-password/staff-created-account
    /// email) that has never needed a file upload.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithoutAttachmentsAsync(
        string to, string subject, string body, CancellationToken cancellationToken)
    {
        // Formspree's standard form submission shape: it forwards whatever fields are posted to
        // the form's configured inbox. "email" is conventionally treated as the reply-to address.
        var payload = new
        {
            email = to,
            subject,
            message = body
        };

        return await _httpClient.PostAsJsonAsync(_settings.FormEndpoint, payload, cancellationToken);
    }

    /// <summary>
    /// Formspree's documented file-upload convention is a <c>multipart/form-data</c> POST with the
    /// ordinary text fields plus one file part per attachment. For a single file, Formspree accepts
    /// any field name and surfaces it as an attachment on the notification email; for multiple
    /// files under the same field name, Formspree only reliably forwards the last one it sees, so
    /// each attachment here gets its own indexed field name (<c>attachment0</c>, <c>attachment1</c>,
    /// ...) to guarantee all of them actually arrive, even though this project currently only ever
    /// sends a single invoice PDF.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithAttachmentsAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(to), "email" },
            { new StringContent(subject), "subject" },
            { new StringContent(body), "message" }
        };

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            var fileContent = new ByteArrayContent(attachment.Content);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(attachment.ContentType);
            content.Add(fileContent, $"attachment{i}", attachment.FileName);
        }

        return await _httpClient.PostAsync(_settings.FormEndpoint, content, cancellationToken);
    }
}
