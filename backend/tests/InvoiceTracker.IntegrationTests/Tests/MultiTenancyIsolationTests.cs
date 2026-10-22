using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Notifications.Common;
using InvoiceTracker.IntegrationTests.Infrastructure;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Proves the BusinessId-scoping rule holds at the HTTP layer for all three resource kinds named
/// in architecture.md's checklist: a client, an invoice, and (indirectly, via an overdue invoice) a
/// notification created by Business A are unreachable via Business B's JWT.
///
/// Client/Invoice each have a single-record GET, so cross-tenant access there really does come back
/// as a literal 404 (matching CLAUDE.md's "Cross-tenant access returns 404, not 403" rule).
/// Notification has no single-record GET — only a list, scoped by joining to Invoices on
/// BusinessId (see ListNotificationsQueryHandler's own doc comment: "the list equivalent of the
/// project's 'cross-tenant access looks like it doesn't exist' rule"). So for notifications this
/// test asserts Business B's list for Business A's invoice comes back empty (200, zero items)
/// rather than literally 404 — same invisibility guarantee, surfaced the only way this list-only
/// endpoint can surface it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MultiTenancyIsolationTests : IntegrationTestBase
{
    public MultiTenancyIsolationTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task BusinessB_CannotReachBusinessA_Client_Invoice_OrNotification()
    {
        var (clientA, authA) = await RegisterBusinessAsync("tenant-a");
        var (clientB, _) = await RegisterBusinessAsync("tenant-b");

        // Business A creates a client...
        var createClientResponse = await clientA.PostAsJsonAsync("/api/clients", new
        {
            Name = "Business A's Client",
            Email = UniqueEmail("tenant-a-client"),
            Phone = "5551111",
            Address = "1 Tenant A St"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        // ...and an invoice for that client.
        var createInvoiceResponse = await clientA.PostAsJsonAsync("/api/invoices", new
        {
            ClientId = clientDto.Id,
            DueDate = DateTime.UtcNow.AddDays(30),
            TaxRate = 0m,
            Notes = (string?)null,
            LineItems = new[] { new { Description = "Tenant A work", Quantity = 1m, UnitPrice = 50m } }
        });
        createInvoiceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoiceDto = (await createInvoiceResponse.Content.ReadFromJsonAsync<InvoiceTracker.Application.Invoices.Common.InvoiceDto>())!;

        // ...and, indirectly via an overdue invoice, a notification (seeded directly — see
        // IntegrationTestBase.SeedOverdueInvoiceWithNotificationAsync for why: no real Kafka broker
        // runs in this test suite).
        var (_, overdueInvoiceId, _) = await SeedOverdueInvoiceWithNotificationAsync(authA.BusinessId);

        // Business B's JWT must not be able to reach any of the three.
        var clientResponse = await clientB.GetAsync($"/api/clients/{clientDto.Id}");
        clientResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invoiceResponse = await clientB.GetAsync($"/api/invoices/{invoiceDto.Id}");
        invoiceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var notificationResponse = await clientB.GetAsync($"/api/notifications?invoiceId={overdueInvoiceId}");
        notificationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var notificationList = await notificationResponse.Content.ReadFromJsonAsync<PaginatedResult<NotificationDto>>();
        notificationList!.Items.Should().BeEmpty();
        notificationList.TotalCount.Should().Be(0);

        // Sanity check: Business A itself CAN see all three, proving the emptiness above is tenant
        // scoping, not e.g. a broken seed.
        (await clientA.GetAsync($"/api/clients/{clientDto.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.GetAsync($"/api/invoices/{invoiceDto.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var ownNotificationResponse = await clientA.GetAsync($"/api/notifications?invoiceId={overdueInvoiceId}");
        var ownNotificationList = await ownNotificationResponse.Content.ReadFromJsonAsync<PaginatedResult<NotificationDto>>();
        ownNotificationList!.Items.Should().ContainSingle();
    }

    /// <summary>
    /// Covers the mutation endpoints that <c>GetForBusinessOrThrowAsync</c> now guards (see
    /// architecture.md's "Multi-tenancy &amp; authorization" shared get-by-id helper) but that the
    /// read-only test above doesn't touch: Client update/archive, every Invoice status-change
    /// endpoint plus update/archive, and User deactivation. Each of these must 404 for Business B
    /// against a resource that belongs to Business A — the tenant check has to run (and reject)
    /// before any status-transition/edit-lock/role check gets a chance to run, so a cross-tenant
    /// caller can't even learn that the record exists, let alone what state it's in.
    /// </summary>
    [Fact]
    public async Task BusinessB_CannotMutate_BusinessA_ClientInvoiceOrUser()
    {
        var (clientA, _) = await RegisterBusinessAsync("tenant-a-mutate");
        var (clientB, _) = await RegisterBusinessAsync("tenant-b-mutate");

        var createClientResponse = await clientA.PostAsJsonAsync("/api/clients", new
        {
            Name = "Business A's Client",
            Email = UniqueEmail("tenant-a-mutate-client"),
            Phone = "5552222",
            Address = "1 Tenant A St"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var createStaffResponse = await clientA.PostAsJsonAsync("/api/users", new { Email = UniqueEmail("tenant-a-mutate-staff") });
        var staffUser = (await createStaffResponse.Content.ReadFromJsonAsync<InvoiceTracker.Application.Users.Common.UserDto>())!;

        async Task<InvoiceTracker.Application.Invoices.Common.InvoiceDto> CreateDraftInvoiceAsync()
        {
            var response = await clientA.PostAsJsonAsync("/api/invoices", new
            {
                ClientId = clientDto.Id,
                DueDate = DateTime.UtcNow.AddDays(30),
                TaxRate = 0m,
                Notes = (string?)null,
                LineItems = new[] { new { Description = "Tenant A work", Quantity = 1m, UnitPrice = 50m } }
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<InvoiceTracker.Application.Invoices.Common.InvoiceDto>())!;
        }

        // Client update/archive.
        var updateClientResponse = await clientB.PutAsJsonAsync($"/api/clients/{clientDto.Id}", new
        {
            Name = "Hijacked",
            Email = clientDto.Email,
            Phone = "5550000",
            Address = "Nowhere"
        });
        updateClientResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var archiveClientResponse = await clientB.DeleteAsync($"/api/clients/{clientDto.Id}");
        archiveClientResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Invoice update.
        var invoiceForUpdate = await CreateDraftInvoiceAsync();
        var updateInvoiceResponse = await clientB.PutAsJsonAsync($"/api/invoices/{invoiceForUpdate.Id}", new
        {
            ClientId = clientDto.Id,
            DueDate = DateTime.UtcNow.AddDays(60),
            TaxRate = 0m,
            Notes = (string?)null,
            LineItems = new[] { new { Description = "Hijacked line item", Quantity = 1m, UnitPrice = 999m } }
        });
        updateInvoiceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Invoice status-change endpoints — each on its own fresh Draft invoice so the 404 is
        // provably from the tenant check, not from an unrelated status/edit-lock rule.
        var invoiceForSend = await CreateDraftInvoiceAsync();
        var sendResponse = await clientB.PostAsync($"/api/invoices/{invoiceForSend.Id}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invoiceForMarkPaid = await CreateDraftInvoiceAsync();
        var markPaidResponse = await clientB.PostAsync($"/api/invoices/{invoiceForMarkPaid.Id}/mark-paid", null);
        markPaidResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invoiceForCancel = await CreateDraftInvoiceAsync();
        var cancelResponse = await clientB.PostAsync($"/api/invoices/{invoiceForCancel.Id}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Invoice archive.
        var invoiceForArchive = await CreateDraftInvoiceAsync();
        var archiveInvoiceResponse = await clientB.DeleteAsync($"/api/invoices/{invoiceForArchive.Id}");
        archiveInvoiceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // User deactivation.
        var deactivateUserResponse = await clientB.DeleteAsync($"/api/users/{staffUser.Id}");
        deactivateUserResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Sanity check: Business A itself can still exercise each of these against its own
        // records, proving the 404s above are tenant scoping, not e.g. broken routes.
        (await clientA.PutAsJsonAsync($"/api/clients/{clientDto.Id}", new
        {
            Name = "Still Business A's Client",
            Email = clientDto.Email,
            Phone = clientDto.Phone,
            Address = clientDto.Address
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var ownInvoice = await CreateDraftInvoiceAsync();
        (await clientA.PostAsync($"/api/invoices/{ownInvoice.Id}/send", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientA.DeleteAsync($"/api/users/{staffUser.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
