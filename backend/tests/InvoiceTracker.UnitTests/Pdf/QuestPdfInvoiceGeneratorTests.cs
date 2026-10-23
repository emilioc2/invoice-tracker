using FluentAssertions;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Infrastructure.Pdf;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Pdf;

/// <summary>
/// Unit tests for <see cref="QuestPdfInvoiceGenerator"/>'s "Payment Details" section, per
/// architecture.md's "Business payment details" section: rendered near the bottom of the
/// template, after line items/totals, only when <c>Invoice.PaymentDetails</c> is non-empty; the
/// section (heading included) is omitted entirely, not rendered empty, when it's null/empty.
///
/// Per this project's own PDF testing convention (see the integration suite's PdfExportTests,
/// which only asserts content-type + "%PDF" magic bytes rather than visual/content), these tests
/// don't parse the rendered PDF's text content either — QuestPDF's output is a compressed binary
/// format, not something worth hand-parsing here. Instead, they assert the documented
/// include/omit *behavior* indirectly: a non-empty PaymentDetails section adds content (a larger
/// rendered document) relative to an otherwise-identical invoice with none, and null/empty/
/// whitespace-only PaymentDetails all produce byte-identical output to each other (proving the
/// section is omitted the same way for all three, not just literally one of them).
/// </summary>
public class QuestPdfInvoiceGeneratorTests
{
    // Mirrors InvoiceTracker.Infrastructure.DependencyInjection's production registration —
    // QuestPDF throws at generation time otherwise. These tests construct
    // QuestPdfInvoiceGenerator directly rather than through DI, so there's no other place this
    // gets set for them.
    static QuestPdfInvoiceGeneratorTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private static Invoice MakeInvoiceWithLineItem(
        Guid businessId, Guid clientId, string? paymentDetails)
    {
        var invoice = InvoiceSeeding.MakeInvoice(businessId, clientId, InvoiceStatus.Unpaid);
        invoice.PaymentDetails = paymentDetails;
        invoice.LineItems.Add(new InvoiceLineItem
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Description = "Consulting",
            Quantity = 2,
            UnitPrice = 500m,
            LineTotal = 1000m
        });
        return invoice;
    }

    [Fact]
    public async Task GenerateInvoicePdfAsync_WithPaymentDetails_ProducesValidPdf_LargerThanWithout()
    {
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);

        var invoiceWithout = MakeInvoiceWithLineItem(business.Id, client.Id, null);
        var invoiceWith = MakeInvoiceWithLineItem(business.Id, client.Id, "Bank: Acme Bank, Acct: 123456");

        var generator = new QuestPdfInvoiceGenerator();

        var bytesWithout = await generator.GenerateInvoicePdfAsync(invoiceWithout, client, business, CancellationToken.None);
        var bytesWith = await generator.GenerateInvoicePdfAsync(invoiceWith, client, business, CancellationToken.None);

        System.Text.Encoding.ASCII.GetString(bytesWithout, 0, 4).Should().Be("%PDF");
        System.Text.Encoding.ASCII.GetString(bytesWith, 0, 4).Should().Be("%PDF");

        // Rendering the extra "Payment Details" heading + free-text value must add content.
        bytesWith.Length.Should().BeGreaterThan(bytesWithout.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateInvoicePdfAsync_OmitsSection_IdenticallyForNullEmptyAndWhitespace(string? paymentDetails)
    {
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);

        var invoiceNull = MakeInvoiceWithLineItem(business.Id, client.Id, null);
        var invoiceOther = MakeInvoiceWithLineItem(business.Id, client.Id, paymentDetails);

        // Keep every other rendered field identical so the only possible source of a byte
        // difference is the PaymentDetails section itself.
        invoiceOther.InvoiceNumber = invoiceNull.InvoiceNumber;
        invoiceOther.DueDate = invoiceNull.DueDate;
        invoiceOther.IssueDate = invoiceNull.IssueDate;

        var generator = new QuestPdfInvoiceGenerator();

        var bytesNull = await generator.GenerateInvoicePdfAsync(invoiceNull, client, business, CancellationToken.None);
        var bytesOther = await generator.GenerateInvoicePdfAsync(invoiceOther, client, business, CancellationToken.None);

        bytesOther.Should().Equal(bytesNull);
    }
}
