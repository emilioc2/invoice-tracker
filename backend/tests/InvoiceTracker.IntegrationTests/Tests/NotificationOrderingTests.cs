using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InvoiceTracker.Application.Clients.Common;
using InvoiceTracker.Application.Common.Models;
using InvoiceTracker.Application.Notifications.Common;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Infrastructure.Persistence;
using InvoiceTracker.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceTracker.IntegrationTests.Tests;

/// <summary>
/// Regression coverage for the <c>ListNotificationsQueryHandler</c> null-<c>SentAt</c> ordering bug:
/// PostgreSQL's default null-ordering for <c>ORDER BY ... DESC</c> is <c>NULLS FIRST</c>, so a plain
/// <c>OrderByDescending(x => x.Notification.SentAt)</c> puts every still-Pending/Failed row
/// (<c>SentAt = null</c>) ahead of every successfully-sent row, regardless of how long ago the sent
/// row actually happened. This can only be proven against a real Postgres instance — EF Core's
/// InMemory provider and any mocked/in-process LINQ evaluation use .NET's own null-ordering
/// semantics, which differ from Postgres's SQL-level default and would pass even with the bug
/// present. <see cref="IntegrationTestWebApplicationFactory"/> runs against a real
/// Testcontainers-provisioned Postgres, so this is an integration test, not a unit test.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class NotificationOrderingTests : IntegrationTestBase
{
    public NotificationOrderingTests(IntegrationTestWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task NotificationsList_SortsNullSentAtLast_BehindAnEarlierButRealSentAt()
    {
        var (client, auth) = await RegisterBusinessAsync("notif-order");

        var createClientResponse = await client.PostAsJsonAsync("/api/clients", new
        {
            Name = "Notif Ordering Test Client",
            Email = UniqueEmail("notif-order-client"),
            Phone = "5551212",
            Address = "1 Ordering Way"
        });
        var clientDto = (await createClientResponse.Content.ReadFromJsonAsync<ClientDto>())!;

        var invoiceId = await SeedInvoiceAsync(auth.BusinessId!.Value, clientDto.Id, InvoiceStatus.Unpaid, 100m);

        Guid sentNotificationId;
        Guid failedNotificationId;

        using (var scope = Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // A real, chronologically-earlier send that nonetheless must sort ahead of the
            // null-SentAt row below.
            var sentNotification = new Notification
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                Status = NotificationStatus.Sent,
                NotificationType = NotificationType.InvoiceSent,
                SentAt = DateTime.UtcNow.AddDays(-5),
                Channel = "Email",
                Message = "Seeded sent notification",
                SourceEventId = Guid.NewGuid()
            };
            sentNotificationId = sentNotification.Id;

            // A Failed row with no SentAt — must sort last, not first, regardless of how it
            // compares to the row above on any other column.
            var failedNotification = new Notification
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                Status = NotificationStatus.Failed,
                NotificationType = NotificationType.InvoiceSent,
                SentAt = null,
                Channel = "Email",
                Message = "Seeded failed notification",
                SourceEventId = Guid.NewGuid()
            };
            failedNotificationId = failedNotification.Id;

            dbContext.Notifications.AddRange(sentNotification, failedNotification);
            await dbContext.SaveChangesAsync();
        }

        var listResponse = await client.GetAsync($"/api/notifications?invoiceId={invoiceId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<NotificationDto>>();

        list!.Items.Should().HaveCount(2);
        list.Items[0].Id.Should().Be(sentNotificationId);
        list.Items[0].SentAt.Should().NotBeNull();
        list.Items[1].Id.Should().Be(failedNotificationId);
        list.Items[1].SentAt.Should().BeNull();
    }
}
