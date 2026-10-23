using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Invoices.Commands.UpdateInvoice;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="UpdateInvoiceCommandHandler"/>'s handling of
/// <c>Invoice.PaymentDetails</c>, per architecture.md's "Business payment details" section: the
/// field is editable while the invoice is still Draft, and rejected (along with the rest of the
/// payload) once the invoice has moved past Draft — identical to the existing line-items/due-date/
/// client edit lock, with no special-casing for this field.
/// </summary>
public class UpdateInvoiceCommandHandlerTests
{
    private static List<InvoiceLineItemRequest> OneLineItem() =>
        new() { new InvoiceLineItemRequest("Consulting", 2, 500m) };

    [Fact]
    public async Task DraftInvoice_CanHavePaymentDetailsChanged()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);
        invoice.PaymentDetails = "Original payment details";

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new UpdateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var command = new UpdateInvoiceCommand(
            invoice.Id, client.Id, invoice.DueDate, 0m, null, OneLineItem(), "Updated payment details");

        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().Be("Updated payment details");

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.PaymentDetails.Should().Be("Updated payment details");
    }

    [Fact]
    public async Task DraftInvoice_CanClearPaymentDetailsBackToNull()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Draft);
        invoice.PaymentDetails = "Original payment details";

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new UpdateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var command = new UpdateInvoiceCommand(invoice.Id, client.Id, invoice.DueDate, 0m, null, OneLineItem());

        var result = await handler.Handle(command, CancellationToken.None);

        result.PaymentDetails.Should().BeNull();
    }

    [Theory]
    [InlineData(InvoiceStatus.Unpaid)]
    [InlineData(InvoiceStatus.Overdue)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Cancelled)]
    public async Task NonDraftInvoice_RejectsPaymentDetailsChange_AndLeavesItUnchanged(InvoiceStatus startingStatus)
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, startingStatus);
        invoice.PaymentDetails = "Original payment details";

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var handler = new UpdateInvoiceCommandHandler(db, new TestCurrentUserService(business.Id));
        var command = new UpdateInvoiceCommand(
            invoice.Id, client.Id, invoice.DueDate, 0m, null, OneLineItem(), "Attempted change");

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>();

        var reloaded = db.Invoices.Single(i => i.Id == invoice.Id);
        reloaded.PaymentDetails.Should().Be("Original payment details");
    }
}
