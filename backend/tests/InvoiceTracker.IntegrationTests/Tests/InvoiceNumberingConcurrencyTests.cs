using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Proves the row-locking <c>BusinessInvoiceCounter</c> approach (CLAUDE.md's "Per-business invoice
/// numbering" convention; <c>ApplicationDbContext.GetNextInvoiceNumberAsync</c>'s
/// <c>INSERT ... ON CONFLICT ... RETURNING</c>) holds under real contention — genuinely concurrent
/// create-invoice requests for the same business must never produce a duplicate or gapped number.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class InvoiceNumberingConcurrencyTests : IntegrationTestBase
{
    public InvoiceNumberingConcurrencyTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task ConcurrentInvoiceCreation_ForSameBusiness_ProducesNoDuplicateNumbers()
    {
        const int concurrentRequests = 20;

        var (client, _) = await RegisterBusinessAsync("numbering-concurrency");

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Concurrency Test Client",
            Email = UniqueEmail("concurrency-client"),
            Phone = "5552222",
            Address = "1 Concurrency Ave"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        // Fire genuinely concurrent requests — all built before any await, then launched together
        // via Task.WhenAll, all against the same HttpClient/business.
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(i => client.PostAsJsonAsync("/api/invoices", new
            {
                ClientId = clientDto.Id,
                DueDate = DateTime.UtcNow.AddDays(30),
                TaxRate = 0m,
                Notes = $"Concurrent invoice #{i}",
                LineItems = new[] { new { Description = "Line", Quantity = 1m, UnitPrice = 10m } }
            }))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var invoices = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<InvoiceDto>()));
        var invoiceNumbers = invoices.Select(i => i!.InvoiceNumber).ToList();

        invoiceNumbers.Should().HaveCount(concurrentRequests);
        invoiceNumbers.Should().OnlyHaveUniqueItems("concurrent invoice creation must never hand out a duplicate number");

        // No gaps either: the resulting numeric suffixes should be exactly {1, 2, ..., 20} in some order.
        var numericSuffixes = invoiceNumbers
            .Select(n => int.Parse(n.Replace("INV-", string.Empty)))
            .OrderBy(n => n)
            .ToList();
        numericSuffixes.Should().BeEquivalentTo(Enumerable.Range(1, concurrentRequests));
    }
}
