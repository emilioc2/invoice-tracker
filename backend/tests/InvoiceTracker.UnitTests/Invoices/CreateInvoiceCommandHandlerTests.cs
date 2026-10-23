using FluentAssertions;
using InvoiceTracker.Application.Invoices.Commands.CreateInvoice;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="CreateInvoiceCommandHandler"/>'s <c>PaymentDetails</c>
/// snapshot-vs-override logic, per architecture.md's "Business payment details" section: an
/// explicit override on the request wins outright; otherwise the handler snapshots the caller's
/// current <c>Business.PaymentDetails</c> at creation time, and a later change to the business
/// default never retroactively touches an invoice already created.
/// </summary>
public class CreateInvoiceCommandHandlerTests
{
    private static List<InvoiceLineItemRequest> OneLineItem() =>
        new() { new InvoiceLineItemRequest("Consulting", 2, 500m) };

    private static CreateInvoiceCommandHandler MakeHandler(TestApplicationDbContext db, Guid businessId) =>
        new(db, new TestCurrentUserService(businessId));

    [Fact]
    public async Task NoOverrideSupplied_SnapshotsCurrentBusinessDefault()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        business.PaymentDetails = "Bank: Acme Bank, Acct: 123456";
        var client = InvoiceSeeding.MakeClient(business.Id);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = MakeHandler(db, business.Id);
        var command = new CreateInvoiceCommand(client.Id, DateTime.UtcNow.AddDays(30), 0m, null, OneLineItem());

        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().Be("Bank: Acme Bank, Acct: 123456");
    }

    [Fact]
    public async Task NoOverrideSupplied_AndNoBusinessDefault_ResultsInNullPaymentDetails()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = MakeHandler(db, business.Id);
        var command = new CreateInvoiceCommand(client.Id, DateTime.UtcNow.AddDays(30), 0m, null, OneLineItem());

        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().BeNull();
    }

    [Fact]
    public async Task ExplicitOverrideSupplied_TakesPrecedenceOverBusinessDefault()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        business.PaymentDetails = "Bank: Acme Bank, Acct: 123456";
        var client = InvoiceSeeding.MakeClient(business.Id);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = MakeHandler(db, business.Id);
        var command = new CreateInvoiceCommand(
            client.Id, DateTime.UtcNow.AddDays(30), 0m, null, OneLineItem(), "Pay via the attached link instead");

        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().Be("Pay via the attached link instead");
    }

    [Fact]
    public async Task LaterChangeToBusinessDefault_DoesNotRetroactivelyChangeAlreadyCreatedInvoice()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        business.PaymentDetails = "Original default";
        var client = InvoiceSeeding.MakeClient(business.Id);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var handler = MakeHandler(db, business.Id);
        var command = new CreateInvoiceCommand(client.Id, DateTime.UtcNow.AddDays(30), 0m, null, OneLineItem());
        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().Be("Original default");

        // Business default changes after the invoice was already created.
        business.PaymentDetails = "New default";
        await db.SaveChangesAsync();

        var reloadedInvoice = db.Invoices.Single(i => i.Id == result.Id);
        reloadedInvoice.PaymentDetails.Should().Be("Original default");
    }
}
