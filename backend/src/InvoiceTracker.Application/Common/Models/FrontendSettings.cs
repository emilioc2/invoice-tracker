namespace InvoiceTracker.Application.Common.Models;

/// <summary>
/// Bound from the "Frontend" configuration section (see appsettings.Development.json for the real
/// local dev value, appsettings.json for the placeholder). Lives in the Application layer — not
/// Infrastructure, unlike e.g. <c>ResendSettings</c> — because <c>ForgotPasswordCommandHandler</c>
/// (an Application-layer handler) needs it directly to build a clickable reset-password link;
/// Infrastructure still owns the actual <c>IOptions&lt;FrontendSettings&gt;</c> binding in its
/// DependencyInjection.cs, the same way it owns binding for every other settings class.
/// </summary>
public class FrontendSettings
{
    public string BaseUrl { get; set; } = string.Empty;
}
