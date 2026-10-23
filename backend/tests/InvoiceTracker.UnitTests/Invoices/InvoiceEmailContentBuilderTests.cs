using FluentAssertions;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.UnitTests.TestSupport;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="InvoiceEmailContentBuilder"/>'s payment-details line, per
/// architecture.md's "Business payment details" section: both <see cref="InvoiceEmailContentBuilder.Build"/>
/// (Send/Resend) and <see cref="InvoiceEmailContentBuilder.BuildOverdueReminder"/> include the line
/// when non-empty, and omit it entirely (body byte-for-byte unchanged) when null/empty.
/// </summary>
public class InvoiceEmailContentBuilderTests
{
    [Fact]
    public void Build_FormatsAmountDue_AsZarCurrency_MatchingThePdf()
    {
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);
        invoice.Total = 1100.00m;

        var (_, body) = InvoiceEmailContentBuilder.Build(invoice, client);

        body.Should().Contain($"Amount due: {CurrencyFormatter.Format(1100.00m)}");
    }

    [Fact]
    public void BuildOverdueReminder_FormatsAmountDue_AsZarCurrency_MatchingThePdf()
    {
        var (_, body) = InvoiceEmailContentBuilder.BuildOverdueReminder(
            "INV-0001", "Wayne Enterprises", 1100.00m, DateTime.UtcNow.AddDays(-1));

        body.Should().Contain($"Amount due: {CurrencyFormatter.Format(1100.00m)}");
    }

    [Fact]
    public void Build_IncludesPaymentDetailsLine_WhenInvoiceHasPaymentDetails()
    {
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);
        var invoice = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);
        invoice.PaymentDetails = "Bank: Acme Bank, Acct: 123456";

        var (_, body) = InvoiceEmailContentBuilder.Build(invoice, client);

        body.Should().Contain("Payment details: Bank: Acme Bank, Acct: 123456");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_OmitsPaymentDetailsLine_AndBodyIsByteForByteUnchanged_WhenNullOrEmpty(string? paymentDetails)
    {
        var business = InvoiceSeeding.MakeBusiness();
        var client = InvoiceSeeding.MakeClient(business.Id);

        var invoiceWithout = InvoiceSeeding.MakeInvoice(business.Id, client.Id, InvoiceStatus.Unpaid);
        invoiceWithout.PaymentDetails = null;

        var invoiceWithBlank = InvoiceSeeding.MakeInvoice(
            business.Id, client.Id, InvoiceStatus.Unpaid, invoiceNumber: invoiceWithout.InvoiceNumber, dueDate: invoiceWithout.DueDate);
        invoiceWithBlank.PaymentDetails = paymentDetails;

        var (_, bodyWithoutPaymentDetails) = InvoiceEmailContentBuilder.Build(invoiceWithout, client);
        var (_, bodyWithBlankPaymentDetails) = InvoiceEmailContentBuilder.Build(invoiceWithBlank, client);

        bodyWithBlankPaymentDetails.Should().Be(bodyWithoutPaymentDetails);
        bodyWithBlankPaymentDetails.Should().NotContain("Payment details");
    }

    [Fact]
    public void BuildOverdueReminder_IncludesPaymentDetailsLine_WhenNonEmpty()
    {
        var (_, body) = InvoiceEmailContentBuilder.BuildOverdueReminder(
            "INV-0001", "Wayne Enterprises", 1500.00m, DateTime.UtcNow.AddDays(-1), "Pay via the attached link");

        body.Should().Contain("Payment details: Pay via the attached link");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildOverdueReminder_OmitsPaymentDetailsLine_AndBodyIsByteForByteUnchanged_WhenNullOrEmpty(
        string? paymentDetails)
    {
        var dueDate = DateTime.UtcNow.AddDays(-1);

        var (_, bodyWithout) = InvoiceEmailContentBuilder.BuildOverdueReminder(
            "INV-0001", "Wayne Enterprises", 1500.00m, dueDate);
        var (_, bodyWithBlank) = InvoiceEmailContentBuilder.BuildOverdueReminder(
            "INV-0001", "Wayne Enterprises", 1500.00m, dueDate, paymentDetails);

        bodyWithBlank.Should().Be(bodyWithout);
        bodyWithBlank.Should().NotContain("Payment details");
    }
}
