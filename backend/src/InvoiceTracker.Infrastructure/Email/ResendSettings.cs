namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Bound from the "Resend" configuration section (see appsettings.Development.json for the
/// placeholder shape). <see cref="ApiKey"/> is a secret — sourced from the
/// InvoiceTracker.Api project's user-secrets store locally
/// (<c>dotnet user-secrets set "Resend:ApiKey" "..."</c>) or an environment variable
/// (<c>Resend__ApiKey</c>) in deployed environments, never committed to any appsettings.*.json
/// file. <see cref="FromAddress"/> defaults to Resend's shared sandbox sender
/// (<c>onboarding@resend.dev</c>), which needs no domain/DNS verification but — per
/// requirements.md's documented limitation — can only deliver to the email address that owns the
/// Resend account, not to arbitrary third-party recipients. That's an accepted testing-purposes
/// trade-off, not a bug.
/// </summary>
public class ResendSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "onboarding@resend.dev";
}
