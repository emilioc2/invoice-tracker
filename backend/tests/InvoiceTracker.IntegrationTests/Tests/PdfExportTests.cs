using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// <c>GET /api/invoices/{id}/pdf</c> returns a valid PDF — content-type and `%PDF` magic bytes only,
/// not a visual/content assertion (per architecture.md's checklist item).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class PdfExportTests : IntegrationTestBase
{
    public PdfExportTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetPdf_ReturnsValidPdf()
    {
        var (client, _) = await RegisterBusinessAsync("pdf-export");

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "PDF Test Client",
            Email = UniqueEmail("pdf-client"),
            Phone = "5554444",
            Address = "1 PDF Place"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var createInvoiceResponse = await client.PostAsJsonAsync("/api/invoices", new
        {
            ClientId = clientDto.Id,
            DueDate = DateTime.UtcNow.AddDays(30),
            TaxRate = 0.05m,
            Notes = "PDF export test invoice",
            LineItems = new[] { new { Description = "Design work", Quantity = 3m, UnitPrice = 75m } }
        });
        var invoiceDto = (await createInvoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var pdfResponse = await client.GetAsync($"/api/invoices/{invoiceDto.Id}/pdf");

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        var magicBytes = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        magicBytes.Should().Be("%PDF");
    }
}
