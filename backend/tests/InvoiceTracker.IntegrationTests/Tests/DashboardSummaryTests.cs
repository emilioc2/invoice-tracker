using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Dashboard.Queries.GetDashboardSummary;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Seeds invoices across every status for one business and asserts
/// <c>GET /api/dashboard/summary</c>'s numbers match (architecture.md's checklist item, deferred
/// from the dashboard build-order step to here, where a real HTTP pipeline + real Postgres exist).
/// Draft/Unpaid/Paid/Cancelled are reached via the real HTTP workflow (create/send/mark-paid/cancel);
/// Overdue is seeded directly (see <see cref="IntegrationTestBase.SeedInvoiceAsync"/> — in
/// production it's only ever set by the Kafka scanner, deliberately not running in this suite).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class DashboardSummaryTests : IntegrationTestBase
{
    public DashboardSummaryTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Summary_MatchesSeededInvoicesAcrossAllStatuses()
    {
        var (client, auth) = await RegisterBusinessAsync("dashboard-summary");

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Dashboard Test Client",
            Email = UniqueEmail("dashboard-client"),
            Phone = "555-3333",
            Address = "1 Dashboard Blvd"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        async Task<InvoiceDto> CreateInvoiceAsync(decimal unitPrice)
        {
            var response = await client.PostAsJsonAsync("/api/invoices", new
            {
                ClientId = clientDto.Id,
                DueDate = DateTime.UtcNow.AddDays(30),
                TaxRate = 0m,
                Notes = (string?)null,
                LineItems = new[] { new { Description = "Line", Quantity = 1m, UnitPrice = unitPrice } }
            });
            return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        }

        // Two Draft invoices (100 + 150 — not "outstanding", nothing sent yet).
        await CreateInvoiceAsync(100m);
        await CreateInvoiceAsync(150m);

        // One Unpaid invoice (200 — outstanding).
        var unpaidInvoice = await CreateInvoiceAsync(200m);
        await client.PostAsync($"/api/invoices/{unpaidInvoice.Id}/send", null);

        // One Paid invoice (300 — resolved, not outstanding).
        var paidInvoice = await CreateInvoiceAsync(300m);
        await client.PostAsync($"/api/invoices/{paidInvoice.Id}/send", null);
        await client.PostAsync($"/api/invoices/{paidInvoice.Id}/mark-paid", null);

        // One Cancelled invoice (400 — resolved, not outstanding).
        var cancelledInvoice = await CreateInvoiceAsync(400m);
        await client.PostAsync($"/api/invoices/{cancelledInvoice.Id}/cancel", null);

        // Two Overdue invoices (500 + 600 — outstanding), seeded directly since only the (disabled)
        // Kafka scanner sets this status in production.
        await SeedInvoiceAsync(auth.BusinessId, clientDto.Id, InvoiceStatus.Overdue, 500m);
        await SeedInvoiceAsync(auth.BusinessId, clientDto.Id, InvoiceStatus.Overdue, 600m);

        var summaryResponse = await client.GetAsync("/api/dashboard/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<DashboardSummaryDto>();

        summary!.OverdueCount.Should().Be(2);
        // Outstanding = Unpaid (200) + Overdue (500 + 600) = 1300.
        summary.OutstandingTotal.Should().Be(1300m);

        summary.CountsByStatus.Should().HaveCount(5);
        summary.CountsByStatus.Single(s => s.Status == "Draft").Count.Should().Be(2);
        summary.CountsByStatus.Single(s => s.Status == "Unpaid").Count.Should().Be(1);
        summary.CountsByStatus.Single(s => s.Status == "Paid").Count.Should().Be(1);
        summary.CountsByStatus.Single(s => s.Status == "Cancelled").Count.Should().Be(1);
        summary.CountsByStatus.Single(s => s.Status == "Overdue").Count.Should().Be(2);
    }

    [Fact]
    public async Task Summary_ForBusinessWithNoInvoices_ReturnsZeroes()
    {
        var (client, _) = await RegisterBusinessAsync("dashboard-empty");

        var response = await client.GetAsync("/api/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryDto>();

        summary!.OverdueCount.Should().Be(0);
        summary.OutstandingTotal.Should().Be(0m);
        summary.CountsByStatus.Should().OnlyContain(s => s.Count == 0);
    }
}
