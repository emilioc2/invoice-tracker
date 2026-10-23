using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that boots the full real HTTP pipeline
/// (routing, auth, MediatR, validation, EF Core) against a real, Testcontainers-provisioned
/// Postgres instance, with real EF Core migrations applied — while guaranteeing the test run never
/// attempts to reach a real Kafka broker or send a real email. Shared across the whole integration
/// test assembly via <see cref="IntegrationTestCollection"/> (one container, started once) so each
/// test class doesn't pay the container-startup cost itself; tests stay isolated from each other by
/// each registering its own Business with a unique email, not by resetting the database between
/// tests (see IntegrationTestBase for that convention).
///
/// How the "no real Kafka" requirement is met:
/// 1. All three Kafka-touching <c>IHostedService</c> registrations
///    (<c>OverdueInvoiceScannerService</c>, <c>OutboxRelayService</c> from the Api project;
///    <c>OverdueNotificationConsumer</c> from Infrastructure) are removed entirely in
///    <see cref="ConfigureWebHost"/> below, so none of their <c>ExecuteAsync</c> loops ever start —
///    in particular, <c>OverdueNotificationConsumer</c>'s blocking <c>ConsumerBuilder...Build()</c>
///    call (made inside <c>ExecuteAsync</c>, not its constructor) never runs, so it can't spin up a
///    background thread trying to reach a broker that isn't there.
/// 2. The singleton <c>IEventPublisher</c> (<c>KafkaEventPublisher</c>, which builds a real
///    Confluent.Kafka producer in its constructor) is also replaced with
///    <see cref="NoOpEventPublisher"/>, as a second line of defense — belt-and-braces alongside #1,
///    since nothing should resolve <c>IEventPublisher</c> once <c>OutboxRelayService</c> is gone.
/// </summary>
public class IntegrationTestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("invoicetracker_test")
        .WithUsername("invoicetracker_test")
        .WithPassword("invoicetracker_test")
        // Testcontainers picks an ephemeral host port by default, so this never collides with the
        // docker-compose Postgres container (which publishes the fixed 5432) even if that one
        // happens to be running at the same time.
        .Build();

    public TestEmailSender EmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Appended after appsettings.json/appsettings.Development.json, so this wins —
            // points the app at the Testcontainers Postgres instance instead of the
            // docker-compose one, regardless of what's running locally.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _dbContainer.GetConnectionString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // --- 1. No real Kafka: remove every hosted service, so OverdueInvoiceScannerService,
            // OutboxRelayService and OverdueNotificationConsumer never start.
            services.RemoveAll<IHostedService>();

            // --- 2. Belt-and-braces: replace the real Kafka producer adapter with a no-op.
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher, NoOpEventPublisher>();

            // --- 3. Never a real Resend call: replace IEmailSender with a fake that records
            // what was "sent" so tests can assert on it (e.g. reading a password-reset token back
            // out of a captured email body). Registered as the single shared instance (via the
            // factory's own EmailSender property) so tests can inspect it directly.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // --- 4. Real Postgres via Testcontainers, not the docker-compose instance and not
            // EF Core in-memory — migrations are applied for real in InitializeAsync below.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Accessing Services is what triggers WebApplicationFactory to actually build the host
        // (and run ConfigureWebHost above) — by this point the container is already running, so
        // the connection string captured above is valid.
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Real migrations, not EnsureCreated — this is the one place in the test suite proving the
        // actual migration history applies cleanly to a fresh database.
        await dbContext.Database.MigrateAsync();
    }

    // IAsyncLifetime.DisposeAsync (Task) is implemented explicitly alongside
    // WebApplicationFactory's own inherited IAsyncDisposable.DisposeAsync (ValueTask) — the two
    // have different return types, so there's no member clash; xUnit calls this one after all
    // tests in the collection finish, and it in turn disposes the base factory (which tears down
    // the in-memory TestServer) before stopping the container.
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.StopAsync();
    }
}
