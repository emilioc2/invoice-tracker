namespace InvoiceTracker.Infrastructure.Identity;

/// <summary>
/// Bound from the "Jwt" configuration section (see appsettings.Development.json).
/// </summary>
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
