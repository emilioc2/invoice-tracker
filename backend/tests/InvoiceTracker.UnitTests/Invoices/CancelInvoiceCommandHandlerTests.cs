using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Invoices.Commands.CancelInvoice;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Status-transition unit tests for <see cref="CancelInvoiceCommandHandler"/>
/// (Draft/Unpaid -> Cancelled) per architecture.md's test plan: both valid starting statuses
/// succeed; every other starting status is rejected — explicitly including Paid -> Cancelled,
/// which requirements.md calls out by name as never allowed.
/// </summary>
public class CancelInvoiceCommandHandlerTests
{
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Unpaid)]
    public async Task DraftOrUnpaidInvoice_IsCancelledSuccessfully(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new CancelInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var result = await handler.Handle(new CancelInvoiceCommand(invoice.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(InvoiceStatus.Cancelled));

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Cancelled);
    }

    [Fact]
    public async Task PaidInvoice_CannotBeCancelled_ThrowsBadRequest()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Paid);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new CancelInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));

        var act = () => handler.Handle(new CancelInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Theory]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task OverdueOrAlreadyCancelledInvoice_CannotBeCancelledAgain_ThrowsBadRequest(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new CancelInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));

        var act = () => handler.Handle(new CancelInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(startingStatus);
    }

    [Fact]
    public async Task InvoiceFromAnotherBusiness_IsTreatedAsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);

        db.Businesses.AddRange(business, otherBusiness);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new CancelInvoiceCommandHandler(db, new TestCurrentUserService(otherBusiness.Id));

        var act = () => handler.Handle(new CancelInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
