namespace InvoiceTracker.Infrastructure.Email;

/// <summary>
/// Bound from the "Formspree" configuration section (see appsettings.Development.json). The
/// endpoint there is a documented PLACEHOLDER — no real Formspree form exists yet for this project.
/// </summary>
public class FormspreeSettings
{
    public string FormEndpoint { get; set; } = string.Empty;
}
