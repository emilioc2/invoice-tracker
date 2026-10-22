using System.Net.Http.Headers;
using System.Net.Http.Json;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Shared base for every integration test class — all share the one
/// <see cref="IntegrationTestWebApplicationFactory"/>/Postgres container via
/// <see cref="IntegrationTestCollection"/>. Tests stay isolated from each other by each registering
/// its own uniquely-named Business (see <see cref="UniqueEmail"/>), not by resetting the database
/// between tests.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase
{
    protected readonly IntegrationTestWebApplicationFactory Factory;

    protected IntegrationTestBase(IntegrationTestWebApplicationFactory factory)
    {
        Factory = factory;
    }

    /// <summary>A fresh, unauthenticated client hitting the real HTTP pipeline.</summary>
    protected HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>Guarantees no cross-test collisions on unique columns (Business/User email).</summary>
    protected static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// A short, (per-business) unique-enough InvoiceNumber for directly-seeded invoices — the
    /// column is <c>varchar(20)</c> (see <c>InvoiceConfiguration</c>), so this can't reuse a full
    /// Guid like <see cref="UniqueEmail"/> does.
    /// </summary>
    private static string SeededInvoiceNumber(Guid invoiceId) => $"INV-S{invoiceId:N}"[..20];

    protected static HttpClient Authorize(HttpClient client, string? accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Registers a brand-new Business + Owner via the real <c>POST /api/auth/register</c> endpoint
    /// and returns an already-authorized <see cref="HttpClient"/> plus the auth result (BusinessId,
    /// UserId, etc.) for assertions.
    /// </summary>
    protected async Task<(HttpClient Client, AuthResultDto Auth)> RegisterBusinessAsync(
        string? businessNamePrefix = null,
        string? password = null)
    {
        var client = CreateClient();
        var ownerEmail = UniqueEmail(businessNamePrefix ?? "owner");

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            BusinessName = $"{businessNamePrefix ?? "Business"} {Guid.NewGuid():N}",
            BusinessEmail = UniqueEmail("biz"),
            OwnerEmail = ownerEmail,
            Password = password ?? "P@ssw0rd123!"
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        ArgumentNullException.ThrowIfNull(auth);

        Authorize(client, auth.AccessToken);
        return (client, auth);
    }

    /// <summary>
    /// Directly inserts a single invoice with an arbitrary <see cref="InvoiceStatus"/> for an
    /// existing client — used where the real HTTP workflow can't reach that status on its own
    /// (namely <c>Overdue</c>, which in production is only ever set by
    /// <c>OverdueInvoiceScannerService</c>, deliberately not running in this test suite — see
    /// <see cref="IntegrationTestWebApplicationFactory"/>).
    /// </summary>
    protected async Task<Guid> SeedInvoiceAsync(Guid businessId, Guid clientId, InvoiceStatus status, decimal total)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var invoiceId = Guid.NewGuid();

        var invoice = new Invoice
        {
            Id = invoiceId,
            BusinessId = businessId,
            ClientId = clientId,
            InvoiceNumber = SeededInvoiceNumber(invoiceId),
            CreatedAtDate = now.AddDays(-30),
            IssueDate = status == InvoiceStatus.Draft ? null : now.AddDays(-30),
            DueDate = now.AddDays(-10),
            Subtotal = total,
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = total,
            LastModifiedDate = now,
            LineItems = new List<InvoiceLineItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoiceId,
                    Description = "Seeded line item",
                    Quantity = 1m,
                    UnitPrice = total,
                    LineTotal = total
                }
            }
        };

        // Status's setter is private (see Invoice.TransitionTo) — fast-forward from the default
        // Draft to the requested starting status via legal transitions only.
        FastForwardInvoiceStatus(invoice, status);

        dbContext.Invoices.Add(invoice);

        await dbContext.SaveChangesAsync();

        return invoiceId;
    }

    /// <summary>
    /// Shared by the direct-seed helpers below: moves a freshly-constructed (Draft) invoice to an
    /// arbitrary starting <see cref="InvoiceStatus"/> via <see cref="Invoice.TransitionTo"/> only
    /// — every status is reachable from Draft in at most two legal steps.
    /// </summary>
    private static void FastForwardInvoiceStatus(Invoice invoice, InvoiceStatus target)
    {
        if (target == InvoiceStatus.Draft)
        {
            return;
        }

        invoice.TransitionTo(InvoiceStatus.Unpaid);

        if (target != InvoiceStatus.Unpaid)
        {
            invoice.TransitionTo(target);
        }
    }

    /// <summary>
    /// Directly inserts a Business, active Client, Overdue Invoice and a resulting Notification —
    /// simulating, without a real Kafka broker, the end state the
    /// scanner/outbox/consumer pipeline would eventually produce for an overdue invoice. The
    /// pipeline components themselves are covered by unit tests against mocked
    /// IEventPublisher/IEmailSender (see architecture.md's Kafka-testing decision); this seed exists
    /// purely so the multi-tenancy isolation test has a real Notification row to probe.
    /// </summary>
    protected async Task<(Guid ClientId, Guid InvoiceId, Guid NotificationId)> SeedOverdueInvoiceWithNotificationAsync(Guid businessId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        var client = new Client
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            Name = "Seeded Overdue Client",
            Email = UniqueEmail("client"),
            Phone = "555-0100",
            Address = "1 Test Street",
            CreatedAtDate = now,
            LastModifiedDate = now
        };
        dbContext.Clients.Add(client);

        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            BusinessId = businessId,
            ClientId = client.Id,
            InvoiceNumber = SeededInvoiceNumber(invoiceId),
            CreatedAtDate = now.AddDays(-30),
            IssueDate = now.AddDays(-30),
            DueDate = now.AddDays(-10),
            Subtotal = 100m,
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = 100m,
            LastModifiedDate = now,
            LineItems = new List<InvoiceLineItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoiceId,
                    Description = "Seeded line item",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    LineTotal = 100m
                }
            }
        };
        FastForwardInvoiceStatus(invoice, InvoiceStatus.Overdue);
        dbContext.Invoices.Add(invoice);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Status = NotificationStatus.Sent,
            NotificationType = NotificationType.OverdueReminder,
            SentAt = now,
            Channel = "Email",
            Message = "Seeded overdue notification",
            SourceEventId = Guid.NewGuid()
        };
        dbContext.Notifications.Add(notification);

        await dbContext.SaveChangesAsync();

        return (client.Id, invoiceId, notification.Id);
    }
}
