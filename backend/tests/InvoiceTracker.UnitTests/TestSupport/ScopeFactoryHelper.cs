using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.UnitTests.TestSupport;

/// <summary>
/// Builds an <see cref="IServiceScopeFactory"/> backed by a single shared in-memory database, so
/// the Kafka BackgroundServices under test (which each create their own DI scope internally via
/// <c>IServiceScopeFactory.CreateScope()</c>, exactly like production) can be unit-tested without
/// a real Postgres — matching how <c>OverdueInvoiceScannerService</c>/<c>OutboxRelayService</c>/
/// <c>OverdueNotificationConsumer</c> are actually wired via DI in Program.cs/DependencyInjection.cs.
/// </summary>
public static class ScopeFactoryHelper
{
    public static IServiceScopeFactory BuildScopeFactory(
        Action<IServiceCollection>? configureAdditionalServices = null)
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();

        services.AddDbContext<TestApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<TestApplicationDbContext>());

        configureAdditionalServices?.Invoke(services);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
