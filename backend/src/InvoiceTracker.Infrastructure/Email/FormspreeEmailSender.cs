using System.Net.Http.Json;
using InvoiceTracker.Application.Common.Interfaces;
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

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Formspree's standard form submission shape: it forwards whatever fields are posted to
        // the form's configured inbox. "email" is conventionally treated as the reply-to address.
        var payload = new
        {
            email = to,
            subject,
            message = body
        };

        using var response = await _httpClient.PostAsJsonAsync(_settings.FormEndpoint, payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Formspree request to {_settings.FormEndpoint} failed with status {(int)response.StatusCode}: {responseBody}");
        }

        _logger.LogInformation("[FormspreeEmailSender] Sent to {To} | Subject: {Subject}", to, subject);
    }
}
