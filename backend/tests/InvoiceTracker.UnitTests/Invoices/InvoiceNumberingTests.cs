using FluentAssertions;
using InvoiceTracker.Application.Invoices.Commands.CreateInvoice;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for the "INV-0001 -> INV-0002" sequencing/formatting logic in
/// <see cref="CreateInvoiceCommandHandler"/>, exercised in isolation via EF Core's in-memory
/// provider. Per architecture.md's test plan, concurrency-safety of the underlying row-locking
/// counter is explicitly integration-test territory, not unit — these tests only cover the
/// single-threaded sequencing/padding/format behavior.
/// </summary>
public class InvoiceNumberingTests
{
    private static CreateInvoiceCommand MakeCommand(Guid clientId) => new(
        clientId,
        DateTime.UtcNow.AddDays(14),
        0m,
        null,
        new List<InvoiceLineItemRequest> { new("Flat fee", 1m, 100m) });

    [Fact]
    public async Task FirstInvoiceForABusiness_IsNumberedInv0001()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = new CreateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var result = await handler.Handle(MakeCommand(client.Id), CancellationToken.None);

        result.InvoiceNumber.Should().Be("INV-0001");
    }

    [Fact]
    public async Task SubsequentInvoicesForTheSameBusiness_IncrementSequentially_WithZeroPadding()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = new CreateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));

        var first = await handler.Handle(MakeCommand(client.Id), CancellationToken.None);
        var second = await handler.Handle(MakeCommand(client.Id), CancellationToken.None);
        var third = await handler.Handle(MakeCommand(client.Id), CancellationToken.None);

        first.InvoiceNumber.Should().Be("INV-0001");
        second.InvoiceNumber.Should().Be("INV-0002");
        third.InvoiceNumber.Should().Be("INV-0003");
    }

    [Fact]
    public async Task EachBusiness_HasItsOwnIndependentNumberingSequence()
    {
        using var db = TestApplicationDbContext.Create();
        var businessA = InvoiceSeeding.MakeBusiness("Business A", "a@example.com");
        var businessB = InvoiceSeeding.MakeBusiness("Business B", "b@example.com");
        var clientA = InvoiceSeeding.MakeClient(businessA.Id);
        var clientB = InvoiceSeeding.MakeClient(businessB.Id);
        db.Businesses.AddRange(businessA, businessB);
        db.Clients.AddRange(clientA, clientB);
        await db.SaveChangesAsync();

        var handlerA = new CreateInvoiceCommandHandler(db, new TestCurrentUserService(businessA.Id));
        var handlerB = new CreateInvoiceCommandHandler(db, new TestCurrentUserService(businessB.Id));

        // Business A creates two invoices first...
        var aFirst = await handlerA.Handle(MakeCommand(clientA.Id), CancellationToken.None);
        var aSecond = await handlerA.Handle(MakeCommand(clientA.Id), CancellationToken.None);
        // ...then Business B creates its first — must still start at INV-0001, not continue A's sequence.
        var bFirst = await handlerB.Handle(MakeCommand(clientB.Id), CancellationToken.None);

        aFirst.InvoiceNumber.Should().Be("INV-0001");
        aSecond.InvoiceNumber.Should().Be("INV-0002");
        bFirst.InvoiceNumber.Should().Be("INV-0001");
    }

    [Fact]
    public async Task NumberingReachesFiveDigitsOfPadding_FormatsWithExtraDigitsRatherThanTruncating()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        db.Businesses.Add(business);
        db.Clients.Add(client);
        // Seed the counter just below the 4-digit rollover so we don't need 10,000 real calls.
        db.BusinessInvoiceCounters.Add(new InvoiceTracker.Domain.Entities.BusinessInvoiceCounter
        {
            BusinessId = business.Id,
            LastNumber = 9999
        });
        await db.SaveChangesAsync();

        var handler = new CreateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var result = await handler.Handle(MakeCommand(client.Id), CancellationToken.None);

        result.InvoiceNumber.Should().Be("INV-10000");
    }
}
