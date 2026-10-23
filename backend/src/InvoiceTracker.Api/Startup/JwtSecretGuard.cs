namespace InvoiceTracker.Api.Startup;

/// <summary>
/// Refuses to let the app start outside <c>Development</c> with an unset or placeholder JWT signing
/// secret. <c>appsettings.Development.json</c> ships a clearly-commented dev-only placeholder
/// (<see cref="DevPlaceholderSecret"/>) so local dev/integration tests work out of the box — but
/// nothing about config binding stops that same placeholder from silently working in a non-Development
/// environment too, which would mean every JWT in that environment is signed with a secret that's
/// public in this repo's history. <see cref="EnsureConfigured"/> is called from <c>Program.cs</c>
/// after configuration is built but before the app starts serving requests.
///
/// Pulled out as a small static method (rather than inlined in <c>Program.cs</c>) so its condition
/// logic — not actual process startup/shutdown — is directly unit-testable.
/// </summary>
public static class JwtSecretGuard
{
    /// <summary>
    /// Must match <c>Jwt:Secret</c> in <c>appsettings.Development.json</c> exactly.
    /// </summary>
    public const string DevPlaceholderSecret = "dev-only-placeholder-secret-not-for-production-use-1234567890";

    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Throws if <paramref name="environmentName"/> is anything other than
    /// <see cref="DevelopmentEnvironmentName"/> and <paramref name="configuredSecret"/> is missing,
    /// empty, or exactly the known dev placeholder.
    /// </summary>
    public static void EnsureConfigured(string? environmentName, string? configuredSecret)
    {
        var isDevelopment = string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredSecret) || configuredSecret == DevPlaceholderSecret)
        {
            throw new InvalidOperationException(
                $"Refusing to start: no real 'Jwt:Secret' is configured for environment '{environmentName}'. " +
                "The configured value is either missing or the known Development-only placeholder from " +
                "appsettings.Development.json, which is public in this repo's source control and must never " +
                "be used to sign tokens outside local Development. Set a real secret via an environment " +
                "variable (e.g. Jwt__Secret) or a secrets manager before deploying to this environment.");
        }
    }
}
