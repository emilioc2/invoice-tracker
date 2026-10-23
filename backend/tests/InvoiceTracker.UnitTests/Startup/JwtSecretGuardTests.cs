using FluentAssertions;
using InvoiceTracker.Api.Startup;

namespace InvoiceTracker.UnitTests.Startup;

/// <summary>
/// Unit tests for <see cref="JwtSecretGuard.EnsureConfigured"/> — the condition logic behind the
/// Program.cs startup guard that refuses to let the app start outside Development with an
/// unset/placeholder JWT signing secret. Tests the pure condition directly rather than spinning up a
/// real host and asserting on process startup/shutdown.
/// </summary>
public class JwtSecretGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")] // environment name comparison is case-insensitive
    public void NonDevelopmentEnvironment_WithThePlaceholderSecret_Throws(string environmentName)
    {
        var act = () => JwtSecretGuard.EnsureConfigured(environmentName, JwtSecretGuard.DevPlaceholderSecret);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NonDevelopmentEnvironment_WithAMissingOrEmptySecret_Throws(string? secret)
    {
        var act = () => JwtSecretGuard.EnsureConfigured("Production", secret);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NonDevelopmentEnvironment_WithARealSecret_DoesNotThrow()
    {
        var act = () => JwtSecretGuard.EnsureConfigured("Production", "a-real-secret-from-an-env-var-or-secrets-manager");

        act.Should().NotThrow();
    }

    [Fact]
    public void DevelopmentEnvironment_WithThePlaceholderSecret_DoesNotThrow()
    {
        var act = () => JwtSecretGuard.EnsureConfigured("Development", JwtSecretGuard.DevPlaceholderSecret);

        act.Should().NotThrow();
    }

    [Fact]
    public void DevelopmentEnvironment_WithAMissingSecret_DoesNotThrow()
    {
        // The guard is only concerned with non-Development environments — Development is left to
        // behave exactly as it does today (JwtBearer/JwtTokenGenerator would fail on their own if
        // the secret were genuinely unusable, but that's unrelated to this guard).
        var act = () => JwtSecretGuard.EnsureConfigured("Development", null);

        act.Should().NotThrow();
    }
}
