using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Commands.SendInvoice;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Invoice CRUD, including the Draft-only edit lock (CLAUDE.md's "Invoice edit lock" convention)
/// and the archive-not-delete behavior.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class InvoiceCrudTests : IntegrationTestBase
{
    public InvoiceCrudTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    private static async Task<Guid> CreateActiveClientAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Invoice Test Client",
            Email = $"invoice-client-{Guid.NewGuid():N}@example.com",
            Phone = "5550000",
            Address = "1 Invoice Way"
        });
        var dto = (await response.Content.ReadFromJsonAsync<ClientDto>())!;
        return dto.Id;
    }

    private static object NewInvoiceRequest(Guid clientId, DateTime? dueDate = null) => new
    {
        ClientId = clientId,
        DueDate = dueDate ?? DateTime.UtcNow.AddDays(30),
        TaxRate = 0.1m,
        Notes = "Integration test invoice",
        LineItems = new[]
        {
            new { Description = "Consulting", Quantity = 2m, UnitPrice = 50m }
        }
    };

    [Fact]
    public async Task Create_ComputesTotalsServerSide_AndStartsAsDraft()
    {
        var (client, _) = await RegisterBusinessAsync("invoice-create");
        var clientId = await CreateActiveClientAsync(client);

        var response = await client.PostAsJsonAsync("/api/invoices", NewInvoiceRequest(clientId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var invoice = await response.Content.ReadFromJsonAsync<InvoiceDto>();
        invoice!.Status.Should().Be("Draft");
        invoice.IssueDate.Should().BeNull();
        invoice.Subtotal.Should().Be(100m);
        invoice.TaxAmount.Should().Be(10m);
        invoice.Total.Should().Be(110m);
        invoice.InvoiceNumber.Should().StartWith("INV-");
    }

    [Fact]
    public async Task Update_WhileDraft_Succeeds()
    {
        var (client, _) = await RegisterBusinessAsync("invoice-update-draft");
        var clientId = await CreateActiveClientAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/invoices", NewInvoiceRequest(clientId));
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var updateResponse = await client.PutAsJsonAsync($"/api/invoices/{created.Id}", new
        {
            ClientId = clientId,
            DueDate = DateTime.UtcNow.AddDays(45),
            TaxRate = 0.2m,
            Notes = "Updated notes",
            LineItems = new[] { new { Description = "Updated line", Quantity = 1m, UnitPrice = 200m } }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        updated!.Subtotal.Should().Be(200m);
        updated.Notes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task Update_AfterSend_IsLockedWithBadRequest()
    {
        var (client, _) = await RegisterBusinessAsync("invoice-edit-lock");
        var clientId = await CreateActiveClientAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/invoices", NewInvoiceRequest(clientId));
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var sendResponse = await client.PostAsync($"/api/invoices/{created.Id}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = await sendResponse.Content.ReadFromJsonAsync<SendInvoiceResult>();
        sent!.Invoice.Status.Should().Be("Unpaid");
        sent.Invoice.IssueDate.Should().NotBeNull();

        // Once Unpaid, the edit lock kicks in — PUT must fail, not silently succeed.
        var updateResponse = await client.PutAsJsonAsync($"/api/invoices/{created.Id}", new
        {
            ClientId = clientId,
            DueDate = DateTime.UtcNow.AddDays(99),
            TaxRate = 0.5m,
            Notes = "Should not be allowed",
            LineItems = new[] { new { Description = "Nope", Quantity = 1m, UnitPrice = 1m } }
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archive_HidesFromDefaultList_ButRecordStillExists()
    {
        var (client, _) = await RegisterBusinessAsync("invoice-archive");
        var clientId = await CreateActiveClientAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/invoices", NewInvoiceRequest(clientId));
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var archiveResponse = await client.DeleteAsync($"/api/invoices/{created.Id}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync("/api/invoices?pageSize=100");
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<InvoiceDto>>();
        list!.Items.Should().NotContain(i => i.Id == created.Id);

        var getResponse = await client.GetAsync($"/api/invoices/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        fetched!.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FullStatusWorkflow_DraftToSendToPaid_Succeeds()
    {
        var (client, _) = await RegisterBusinessAsync("invoice-workflow");
        var clientId = await CreateActiveClientAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/invoices", NewInvoiceRequest(clientId));
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        (await client.PostAsync($"/api/invoices/{created.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var paidResponse = await client.PostAsync($"/api/invoices/{created.Id}/mark-paid", null);
        paidResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paid = await paidResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        paid!.Status.Should().Be("Paid");

        // Cancel is not allowed once Paid.
        var cancelResponse = await client.PostAsync($"/api/invoices/{created.Id}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
