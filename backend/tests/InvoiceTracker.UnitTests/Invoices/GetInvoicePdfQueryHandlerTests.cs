using FluentAssertions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Queries.GetInvoicePdf;
using InvoiceTracker.UnitTests.TestSupport;
using NSubstitute;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="GetInvoicePdfQueryHandler"/> — now a thin adapter over the shared
/// <see cref="IInvoicePdfService"/> (see InvoicePdfServiceTests for the detailed
/// query/multi-tenancy coverage that used to live here, back when the handler assembled the query
/// itself). This suite only checks the handler delegates with the caller's BusinessId and maps the
/// result to the expected filename shape.
/// </summary>
public class GetInvoicePdfQueryHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToInvoicePdfService_WithCallersBusinessId_AndMapsFileName()
    {
        var businessId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var pdfBytes = new byte[] { 1, 2, 3, 4 };

        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(invoiceId, businessId, Arg.Any<CancellationToken>())
            .Returns(new InvoicePdfGenerationResult(pdfBytes, "INV-0042"));

        var handler = new GetInvoicePdfQueryHandler(new TestCurrentUserService(businessId), pdfService);

        var result = await handler.Handle(new GetInvoicePdfQuery(invoiceId), CancellationToken.None);

        result.Content.Should().BeSameAs(pdfBytes);
        result.FileName.Should().Be("INV-0042.pdf");

        await pdfService.Received(1).GenerateAsync(invoiceId, businessId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PropagatesException_FromInvoicePdfService()
    {
        var businessId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var pdfService = Substitute.For<IInvoicePdfService>();
        pdfService
            .GenerateAsync(invoiceId, businessId, Arg.Any<CancellationToken>())
            .Returns<InvoicePdfGenerationResult>(_ => throw new InvoiceTracker.Application.Common.Exceptions.NotFoundException("Invoice not found."));

        var handler = new GetInvoicePdfQueryHandler(new TestCurrentUserService(businessId), pdfService);

        var act = () => handler.Handle(new GetInvoicePdfQuery(invoiceId), CancellationToken.None);

        await act.Should().ThrowAsync<InvoiceTracker.Application.Common.Exceptions.NotFoundException>();
    }
}
