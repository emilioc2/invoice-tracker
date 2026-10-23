using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Covers the "auth" rate-limiter policy registered in Program.cs (<c>AddRateLimiter</c>,
/// <c>[EnableRateLimiting]</c> on <c>AuthController</c>'s brute-force-sensitive actions) end-to-end
/// against the real HTTP pipeline. Deliberately builds its own short-lived
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>
/// clone with a tiny, test-local override of <c>RateLimiting:Auth:PermitLimit</c>/<c>WindowSeconds</c>
/// — the shared <see cref="IntegrationTestWebApplicationFactory"/>'s own config
/// (appsettings.Development.json) deliberately sets a very high limit so the rest of this suite's
/// many rapid register/login calls never trip it, so exercising the real low production-sized limit
/// needs this test's own isolated host/rate-limiter state instead.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RateLimitingTests : IntegrationTestBase
{
    public RateLimitingTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ForgotPassword_BurstPastTheConfiguredLimit_Returns429ForTheExcessRequests()
    {
        const int permitLimit = 3;

        using var scopedFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Auth:PermitLimit"] = permitLimit.ToString(),
                    ["RateLimiting:Auth:WindowSeconds"] = "60"
                });
            });
        });

        using var client = scopedFactory.CreateClient();

        // forgot-password always returns a generic 200 regardless of whether the email matches a
        // real account (see AuthController), so an arbitrary, non-existent address is safe to spam —
        // no account/business setup needed for this test.
        for (var i = 0; i < permitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "nobody@example.com" });
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1} is within the configured limit of {permitLimit}");
        }

        var rejectedResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "nobody@example.com" });

        rejectedResponse.StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task ForgotPassword_WithinTheConfiguredLimit_IsNeverRateLimited()
    {
        // Sanity check in the opposite direction: a caller that stays within the limit never sees a
        // 429 — guards against an overly aggressive limiter configuration.
        const int permitLimit = 5;

        using var scopedFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Auth:PermitLimit"] = permitLimit.ToString(),
                    ["RateLimiting:Auth:WindowSeconds"] = "60"
                });
            });
        });

        using var client = scopedFactory.CreateClient();

        for (var i = 0; i < permitLimit; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "nobody@example.com" });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
