using FluentAssertions;
using InvoiceTracker.Api.BackgroundServices;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceTracker.UnitTests.Kafka;

/// <summary>
/// Unit tests for <see cref="OverdueInvoiceScannerService"/> per architecture.md's test plan:
/// overdue detection edge cases (due yesterday vs. not-yet-due) and idempotent re-processing
/// (an invoice already flipped to Overdue is never re-selected/re-queued on a later pass). Exercised
/// against EF Core's in-memory provider (no real Postgres), via the private <c>ScanAsync</c> method
/// — see <see cref="PrivateMethodInvoker"/>.
/// </summary>
public class OverdueInvoiceScannerServiceTests
{
    private static OverdueInvoiceScannerService CreateScanner(IServiceScopeFactory scopeFactory)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new OverdueInvoiceScannerService(
            scopeFactory,
            configuration,
            NullLogger<OverdueInvoiceScannerService>.Instance);
    }

    private static async Task ScanAsync(OverdueInvoiceScannerService scanner)
    {
        await PrivateMethodInvoker.InvokeAsync(scanner, "ScanAsync", CancellationToken.None);
    }

    private static (Business business, Client client) SeedBusinessAndClient(IApplicationDbContext db)
    {
        var business = new Business { Id = Guid.NewGuid(), Name = "Acme Co", Email = "acme@example.com" };
        var client = new Client
        {
            Id = Guid.NewGuid(),
            BusinessId = business.Id,
            Name = "Wayne Enterprises",
            Email = "billing@wayne.example",
            Phone = "555-1234",
            Address = "1 Wayne Way",
            CreatedAtDate = DateTime.UtcNow,
            LastModifiedDate = DateTime.UtcNow
        };
        db.Businesses.Add(business);
        db.Clients.Add(client);
        return (business, client);
    }

    private static Invoice MakeInvoice(Guid businessId, Guid clientId, InvoiceStatus status, DateTime dueDate)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            BusinessId = businessId,
            ClientId = clientId,
            InvoiceNumber = "INV-0001",
            CreatedAtDate = DateTime.UtcNow.AddDays(-10),
            IssueDate = DateTime.UtcNow.AddDays(-10),
            DueDate = dueDate,
            Subtotal = 1000m,
            TaxRate = 0m,
            TaxAmount = 0m,
            Total = 1000m,
            LastModifiedDate = DateTime.UtcNow.AddDays(-10)
        };

        // Status's setter is private (see Invoice.TransitionTo) — fast-forward from the default
        // Draft to the requested starting status via legal transitions only.
        if (status != InvoiceStatus.Draft)
        {
            invoice.TransitionTo(InvoiceStatus.Unpaid);
            if (status != InvoiceStatus.Unpaid)
            {
                invoice.TransitionTo(status);
            }
        }

        return invoice;
    }

    [Fact]
    public async Task UnpaidInvoiceDueYesterday_FlipsToOverdue_AndWritesOutboxEntry()
    {
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory();

        Guid invoiceId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var (business, client) = SeedBusinessAndClient(db);
            var invoice = MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid, DateTime.UtcNow.AddDays(-1));
            invoiceId = invoice.Id;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var scanner = CreateScanner(scopeFactory);
        await ScanAsync(scanner);

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.Invoices.SingleAsync(i => i.Id == invoiceId);
        reloaded.Status.Should().Be(InvoiceStatus.Overdue);

        var outbox = await verifyDb.OutboxMessages.ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].Type.Should().Be("InvoiceOverdue");
        outbox[0].ProcessedAt.Should().BeNull();
        outbox[0].Payload.Should().Contain(invoiceId.ToString());
    }

    [Fact]
    public async Task UnpaidInvoiceNotYetDue_IsNotFlipped_AndNoOutboxEntryWritten()
    {
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory();

        Guid invoiceId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var (business, client) = SeedBusinessAndClient(db);
            // Due later today -> not yet overdue.
            var invoice = MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid, DateTime.UtcNow.AddHours(2));
            invoiceId = invoice.Id;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var scanner = CreateScanner(scopeFactory);
        await ScanAsync(scanner);

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.Invoices.SingleAsync(i => i.Id == invoiceId);
        reloaded.Status.Should().Be(InvoiceStatus.Unpaid);

        (await verifyDb.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task InvoiceInNonUnpaidStatus_PastDueDate_IsNeverReprocessed(InvoiceStatus status)
    {
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory();

        Guid invoiceId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var (business, client) = SeedBusinessAndClient(db);
            var invoice = MakeInvoice(business.Id, client.Id, status, DateTime.UtcNow.AddDays(-30));
            invoiceId = invoice.Id;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var scanner = CreateScanner(scopeFactory);
        await ScanAsync(scanner);

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.Invoices.SingleAsync(i => i.Id == invoiceId);
        reloaded.Status.Should().Be(status);

        (await verifyDb.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunningScanTwice_DoesNotReFlipOrDuplicateOutboxEntryForAnAlreadyOverdueInvoice()
    {
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory();

        Guid invoiceId;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var (business, client) = SeedBusinessAndClient(db);
            var invoice = MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid, DateTime.UtcNow.AddDays(-1));
            invoiceId = invoice.Id;
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        var scanner = CreateScanner(scopeFactory);

        // First pass: flips Unpaid -> Overdue, writes one outbox row.
        await ScanAsync(scanner);
        // Second pass: the invoice is now Overdue (no longer matches the scanner's
        // Status == Unpaid filter), so it must not be touched/queued again.
        await ScanAsync(scanner);

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.Invoices.SingleAsync(i => i.Id == invoiceId);
        reloaded.Status.Should().Be(InvoiceStatus.Overdue);

        (await verifyDb.OutboxMessages.CountAsync()).Should().Be(1);
    }
}
