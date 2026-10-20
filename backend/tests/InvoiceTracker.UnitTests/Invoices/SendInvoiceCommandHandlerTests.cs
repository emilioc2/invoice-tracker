using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Invoices.Commands.SendInvoice;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Status-transition unit tests for <see cref="SendInvoiceCommandHandler"/> (Draft -> Unpaid) per
/// architecture.md's test plan: the one valid starting status succeeds, every other status is
/// rejected. Exercised against EF Core's in-memory provider via <see cref="TestApplicationDbContext"/>.
/// </summary>
public class SendInvoiceCommandHandlerTests
{
    [Fact]
    public async Task DraftInvoice_IsSentSuccessfully_BecomesUnpaid_AndSetsIssueDate()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);
        invoice.IssueDate.Should().BeNull();

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new SendInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var result = await handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(InvoiceStatus.Unpaid));
        result.IssueDate.Should().NotBeNull();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.Status.Should().Be(InvoiceStatus.Unpaid);
        reloaded.IssueDate.Should().NotBeNull();
    }

    [Theory]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task NonDraftInvoice_CannotBeSent_ThrowsBadRequest(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new SendInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));

        var act = () => handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

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

        var handler = new SendInvoiceCommandHandler(db, new TestCurrentUserService(otherBusiness.Id));

        var act = () => handler.Handle(new SendInvoiceCommand(invoice.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
