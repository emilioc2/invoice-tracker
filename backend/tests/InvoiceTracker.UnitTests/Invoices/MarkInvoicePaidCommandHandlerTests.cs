using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Invoices.Commands.MarkInvoicePaid;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Status-transition unit tests for <see cref="MarkInvoicePaidCommandHandler"/>
/// (Unpaid/Overdue -> Paid) per architecture.md's test plan: both valid starting statuses
/// succeed, every other starting status is rejected — including the explicit "throws for an
/// invoice not in Unpaid/Overdue" case architecture.md calls out by name.
/// </summary>
public class MarkInvoicePaidCommandHandlerTests
{
    [Theory]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    public async Task UnpaidOrOverdueInvoice_IsMarkedPaidSuccessfully(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new MarkInvoicePaidCommandHandler(db, new TestCurrentUserService(business.Id));
        var result = await handler.Handle(new MarkInvoicePaidCommand(invoice.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(InvoiceStatus.Paid));

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task InvoiceNotUnpaidOrOverdue_ThrowsBadRequest_AndStatusIsUnchanged(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new MarkInvoicePaidCommandHandler(db, new TestCurrentUserService(business.Id));

        var act = () => handler.Handle(new MarkInvoicePaidCommand(invoice.Id), CancellationToken.None);

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
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);

        db.Businesses.AddRange(business, otherBusiness);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new MarkInvoicePaidCommandHandler(db, new TestCurrentUserService(otherBusiness.Id));

        var act = () => handler.Handle(new MarkInvoicePaidCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
