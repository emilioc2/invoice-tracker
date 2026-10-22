using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Invoices.Commands.ResendInvoiceEmail;
using InvoiceTracker.Application.Invoices.Commands.SendInvoice;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Invoices.Common;
using InvoiceTracker.Application.Notifications.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Infrastructure.Persistence;
using InvoiceTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// <c>POST /api/invoices/{id}/send</c> and <c>POST /api/invoices/{id}/resend-email</c> per
/// architecture.md's "Invoice send/resend email delivery" section and its test plan additions.
/// Uses <see cref="IntegrationTestWebApplicationFactory.EmailSender"/>'s <c>ShouldFail</c> toggle
/// to prove the status transition and the Notification row are correct independent of the email
/// outcome — reset back to <c>false</c> after each test that sets it, since the fake sender is a
/// single shared instance for the whole collection (see <see cref="IntegrationTestCollection"/>).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class InvoiceEmailDeliveryTests : IntegrationTestBase
{
    public InvoiceEmailDeliveryTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    private async Task<(HttpClient Client, Guid ClientId)> CreateBusinessWithClientAsync(string prefix)
    {
        var (client, _) = await RegisterBusinessAsync(prefix);

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Email Delivery Test Client",
            Email = UniqueEmail($"{prefix}-client"),
            Phone = "5551234",
            Address = "1 Email Way"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        return (client, clientDto.Id);
    }

    private static async Task<InvoiceDto> CreateDraftInvoiceAsync(HttpClient client, Guid clientId)
    {
        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            ClientId = clientId,
            DueDate = DateTime.UtcNow.AddDays(30),
            TaxRate = 0m,
            Notes = "Email delivery test invoice",
            LineItems = new[] { new { Description = "Work", Quantity = 1m, UnitPrice = 250m } }
        });
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    [Fact]
    public async Task Send_Succeeds_IncludesSentEmailDeliveryStatus()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("send-success");
        var invoice = await CreateDraftInvoiceAsync(client, clientId);

        var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);

        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await sendResponse.Content.ReadFromJsonAsync<SendInvoiceResult>();

        result!.Invoice.Status.Should().Be("Unpaid");
        result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Sent));
        result.EmailDelivery.NotificationId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Send_InvoiceStillTransitionsToUnpaid_EvenWhenEmailSenderFails()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("send-email-fail");
        var invoice = await CreateDraftInvoiceAsync(client, clientId);

        Factory.EmailSender.ShouldFail = true;
        try
        {
            var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);

            sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await sendResponse.Content.ReadFromJsonAsync<SendInvoiceResult>();

            // The transition succeeds purely on its own merits, independent of the email outcome.
            result!.Invoice.Status.Should().Be("Unpaid");
            result.Invoice.IssueDate.Should().NotBeNull();
            result.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Failed));

            var getResponse = await client.GetAsync($"/api/invoices/{invoice.Id}");
            var reloaded = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
            reloaded!.Status.Should().Be("Unpaid");
        }
        finally
        {
            Factory.EmailSender.ShouldFail = false;
        }
    }

    [Fact]
    public async Task ResendEmail_OnDraftInvoice_Returns400()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("resend-draft");
        var invoice = await CreateDraftInvoiceAsync(client, clientId);

        var resendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/resend-email", null);

        resendResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResendEmail_OnNonDraftInvoice_Returns200_AndRecordsANewNotification()
    {
        var (client, clientId) = await CreateBusinessWithClientAsync("resend-nondraft");
        var invoice = await CreateDraftInvoiceAsync(client, clientId);

        var sendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);
        var sendResult = (await sendResponse.Content.ReadFromJsonAsync<SendInvoiceResult>())!;

        var resendResponse = await client.PostAsync($"/api/invoices/{invoice.Id}/resend-email", null);

        resendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resendResult = await resendResponse.Content.ReadFromJsonAsync<ResendInvoiceEmailResult>();

        resendResult!.EmailDelivery.Status.Should().Be(nameof(NotificationStatus.Sent));
        resendResult.EmailDelivery.NotificationId.Should().NotBe(sendResult.EmailDelivery.NotificationId);

        // Invoice status/edit-lock must be untouched by a resend.
        var getResponse = await client.GetAsync($"/api/invoices/{invoice.Id}");
        var reloaded = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        reloaded!.Status.Should().Be("Unpaid");

        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationCount = await dbContext.Notifications.CountAsync(n => n.InvoiceId == invoice.Id);
        notificationCount.Should().Be(2);
    }

    [Fact]
    public async Task NotificationsList_DistinguishesOverdueReminder_FromInvoiceSent()
    {
        var (client, auth) = await RegisterBusinessAsync("notif-type");

        // An OverdueReminder-type row (simulating the Kafka pipeline's eventual output — see
        // SeedOverdueInvoiceWithNotificationAsync's own doc comment for why this is seeded directly
        // rather than driven through a real Kafka broker).
        await SeedOverdueInvoiceWithNotificationAsync(auth.BusinessId);

        // An InvoiceSent-type row via the real Send endpoint.
        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Notif Type Client",
            Email = UniqueEmail("notif-type-client"),
            Phone = "5559999",
            Address = "1 Notif Way"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;
        var invoice = await CreateDraftInvoiceAsync(client, clientDto.Id);
        await client.PostAsync($"/api/invoices/{invoice.Id}/send", null);

        var listResponse = await client.GetAsync("/api/notifications?pageSize=50");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<NotificationDto>>();

        list!.Items.Should().Contain(n => n.NotificationType == nameof(NotificationType.OverdueReminder));
        list.Items.Should().Contain(n => n.NotificationType == nameof(NotificationType.InvoiceSent));
    }
}
