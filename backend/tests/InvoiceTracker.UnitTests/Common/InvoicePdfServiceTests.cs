using FluentAssertions;
using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Services;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Common;

/// <summary>
/// Unit tests for <see cref="InvoicePdfService"/> — the Invoice+LineItems+Client+Business
/// assembly query + IPdfGenerator rendering, extracted from what used to live inline in
/// GetInvoicePdfQueryHandler (see that handler's much simpler tests now). Per architecture.md's
/// "PDF generation — single query" implementation note and the project's multi-tenancy rule
/// (cross-tenant access returns 404, never a 403 or an unhandled exception). Exercised against EF
/// Core's in-memory provider (no real Postgres) with a mocked <see cref="IPdfGenerator"/> (no real
/// QuestPDF rendering).
/// </summary>
public class InvoicePdfServiceTests
{
    private static InvoiceLineItem MakeLineItem(Guid invoiceId, string description, decimal quantity, decimal unitPrice) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = invoiceId,
        Description = description,
        Quantity = quantity,
        UnitPrice = unitPrice,
        LineTotal = quantity * unitPrice
    };

    [Fact]
    public async Task HappyPath_ReturnsPdfBytesAndInvoiceNumber_ForSameBusinessInvoice()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid, invoiceNumber: "INV-0042");
        var lineItem1 = MakeLineItem(invoice.Id, "Consulting", 2, 500m);
        var lineItem2 = MakeLineItem(invoice.Id, "Hosting", 1, 100m);

        db.Businesses.Add(business);
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        db.InvoiceLineItems.AddRange(lineItem1, lineItem2);
        await db.SaveChangesAsync();

        var pdfBytes = new byte[] { 1, 2, 3, 4 };
        var pdfGenerator = Substitute.For<IPdfGenerator>();
        pdfGenerator
            .GenerateInvoicePdfAsync(Arg.Any<Invoice>(), Arg.Any<Client>(), Arg.Any<Business>(), Arg.Any<CancellationToken>())
            .Returns(pdfBytes);

        var service = new InvoicePdfService(db, pdfGenerator);

        var result = await service.GenerateAsync(invoice.Id, business.Id, CancellationToken.None);

        result.Content.Should().BeSameAs(pdfBytes);
        result.InvoiceNumber.Should().Be("INV-0042");

        // Confirm the generator was actually invoked with the correct, fully-loaded graph:
        // the right invoice (with its line items available), the right client, the right business.
        await pdfGenerator.Received(1).GenerateInvoicePdfAsync(
            Arg.Is<Invoice>(i => i.Id == invoice.Id && i.LineItems.Count == 2
                && i.LineItems.Any(li => li.Description == "Consulting")
                && i.LineItems.Any(li => li.Description == "Hosting")),
            Arg.Is<Client>(c => c.Id == client.Id),
            Arg.Is<Business>(b => b.Id == business.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvoiceFromAnotherBusiness_ThrowsNotFound_AndNeverCallsPdfGenerator()
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

        var pdfGenerator = Substitute.For<IPdfGenerator>();

        // Business B's JWT (otherBusiness) requesting Business A's invoice id.
        var service = new InvoicePdfService(db, pdfGenerator);

        var act = () => service.GenerateAsync(invoice.Id, otherBusiness.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await pdfGenerator.DidNotReceive().GenerateInvoicePdfAsync(
            Arg.Any<Invoice>(), Arg.Any<Client>(), Arg.Any<Business>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonExistentInvoiceId_ThrowsNotFound()
    {
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var pdfGenerator = Substitute.For<IPdfGenerator>();
        var service = new InvoicePdfService(db, pdfGenerator);

        var act = () => service.GenerateAsync(Guid.NewGuid(), business.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task InvoiceWhoseClientBelongsToAnotherBusiness_ThrowsNotFound_NotNullReferenceException()
    {
        // Data-integrity edge case per the review's trace request: an invoice whose ClientId points
        // at a client row owned by a *different* business than the invoice/caller. The inner join's
        // client.BusinessId == businessId filter must exclude this row entirely (-> null -> 404),
        // never flow a null client into IPdfGenerator and NRE.
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var otherBusiness = InvoiceSeeding.MakeBusiness("Other Co", "other@example.com");
        var foreignClient = InvoiceSeeding.MakeClient(otherBusiness.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, foreignClient.Id, InvoiceStatus.Unpaid);

        db.Businesses.AddRange(business, otherBusiness);
        db.Clients.Add(foreignClient);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var pdfGenerator = Substitute.For<IPdfGenerator>();
        var service = new InvoicePdfService(db, pdfGenerator);

        var act = () => service.GenerateAsync(invoice.Id, business.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await pdfGenerator.DidNotReceive().GenerateInvoicePdfAsync(
            Arg.Any<Invoice>(), Arg.Any<Client>(), Arg.Any<Business>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvoiceWithNoMatchingBusinessRow_ThrowsNotFound_NotNullReferenceException()
    {
        // Orphaned-reference edge case: invoice.BusinessId has no corresponding Business row at all
        // (shouldn't happen under real FK constraints, but the service must not NRE if it did — the
        // inner join on invoice.BusinessId == business.Id simply yields no match -> null -> 404).
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);

        // Note: business itself is deliberately NOT added to db.Businesses.
        db.Clients.Add(client);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var pdfGenerator = Substitute.For<IPdfGenerator>();
        var service = new InvoicePdfService(db, pdfGenerator);

        var act = () => service.GenerateAsync(invoice.Id, business.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await pdfGenerator.DidNotReceive().GenerateInvoicePdfAsync(
            Arg.Any<Invoice>(), Arg.Any<Client>(), Arg.Any<Business>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvoiceWithNoMatchingClientRow_ThrowsNotFound_NotNullReferenceException()
    {
        // Orphaned-reference edge case: invoice.ClientId has no corresponding Client row at all.
        // The inner join on invoice.ClientId == client.Id yields no match -> null -> 404, not NRE.
        using var db = TestApplicationDbContext.Create();
        var business = InvoiceSeeding.MakeBusiness();
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, Guid.NewGuid(), InvoiceStatus.Unpaid);

        db.Businesses.Add(business);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var pdfGenerator = Substitute.For<IPdfGenerator>();
        var service = new InvoicePdfService(db, pdfGenerator);

        var act = () => service.GenerateAsync(invoice.Id, business.Id, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        await pdfGenerator.DidNotReceive().GenerateInvoicePdfAsync(
            Arg.Any<Invoice>(), Arg.Any<Client>(), Arg.Any<Business>(), Arg.Any<CancellationToken>());
    }
}
