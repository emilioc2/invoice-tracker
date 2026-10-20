using System.Text.Json;
using FluentAssertions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Events;
using InvoiceTracker.Infrastructure.Messaging;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Kafka;

/// <summary>
/// Unit tests for <see cref="OverdueNotificationConsumer"/> per architecture.md's test plan — the
/// three cases that prove the crash-safe idempotency design actually holds:
/// 1. No Notification row exists -> inserts Pending, sends, updates to Sent.
/// 2. Row exists as Sent -> skips, never calls IEmailSender (ordinary at-least-once redelivery).
/// 3. Row exists as Pending -> retries the send using the existing row (this is what proves a
///    crash between "recorded" and "sent" can only ever leave one row behind to retry, never a
///    second Sent row for the same SourceEventId).
///
/// Exercised against a mocked <see cref="IEmailSender"/> (no real Formspree/HTTP call) and EF
/// Core's in-memory provider (no real Kafka broker, no real Postgres) via the private
/// <c>ProcessMessageAsync</c> method directly — this consumer's own Kafka consume loop is
/// infrastructure plumbing around that method, not business logic to re-test here.
/// </summary>
public class OverdueNotificationConsumerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static OverdueNotificationConsumer CreateConsumer(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, Options.Create(new KafkaSettings()), NullLogger<OverdueNotificationConsumer>.Instance);

    private static Task ProcessMessageAsync(OverdueNotificationConsumer consumer, string payload) =>
        PrivateMethodInvoker.InvokeAsync(consumer, "ProcessMessageAsync", payload, CancellationToken.None);

    private static InvoiceOverdueEvent MakeEvent(Guid eventId) => new(
        eventId,
        "InvoiceOverdue",
        DateTime.UtcNow,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "INV-0001",
        Guid.NewGuid(),
        "Acme Co",
        "billing@acme.example",
        DateTime.UtcNow.AddDays(-1),
        1500.00m);

    [Fact]
    public async Task NoExistingNotification_InsertsPendingRow_SendsEmail_ThenUpdatesToSent()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services => services.AddSingleton(emailSender));

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var consumer = CreateConsumer(scopeFactory);
        await ProcessMessageAsync(consumer, payload);

        await emailSender.Received(1).SendAsync(
            @event.ClientEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await db.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        var notification = notifications[0];
        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentAt.Should().NotBeNull();
        notification.InvoiceId.Should().Be(@event.InvoiceId);
        notification.Channel.Should().Be("Email");
    }

    [Fact]
    public async Task ExistingSentNotification_IsSkipped_AndEmailSenderIsNeverCalled()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services => services.AddSingleton(emailSender));

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var existingNotificationId = Guid.NewGuid();
        var originalSentAt = DateTime.UtcNow.AddHours(-1);
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = existingNotificationId,
                InvoiceId = @event.InvoiceId,
                Status = NotificationStatus.Sent,
                SentAt = originalSentAt,
                Channel = "Email",
                Message = "original message",
                SourceEventId = eventId
            });
            await db.SaveChangesAsync();
        }

        var consumer = CreateConsumer(scopeFactory);
        await ProcessMessageAsync(consumer, payload);

        await emailSender.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await verifyDb.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        notifications[0].Id.Should().Be(existingNotificationId);
        notifications[0].Status.Should().Be(NotificationStatus.Sent);
        notifications[0].SentAt.Should().Be(originalSentAt);
    }

    [Fact]
    public async Task ExistingPendingNotification_RetriesSend_UsingTheExistingRow_NoDuplicateInserted()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services => services.AddSingleton(emailSender));

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var existingNotificationId = Guid.NewGuid();
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = existingNotificationId,
                InvoiceId = @event.InvoiceId,
                Status = NotificationStatus.Pending,
                SentAt = null,
                Channel = "Email",
                Message = "a previous attempt crashed before this could be marked Sent",
                SourceEventId = eventId
            });
            await db.SaveChangesAsync();
        }

        var consumer = CreateConsumer(scopeFactory);
        await ProcessMessageAsync(consumer, payload);

        // Proves crash-safety: the retry reuses the existing Pending row rather than inserting a
        // second one, so a crash between "recorded" and "sent" can never produce two Sent rows for
        // the same SourceEventId.
        await emailSender.Received(1).SendAsync(
            @event.ClientEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifications = await verifyDb.Notifications.Where(n => n.SourceEventId == eventId).ToListAsync();

        notifications.Should().ContainSingle();
        notifications[0].Id.Should().Be(existingNotificationId);
        notifications[0].Status.Should().Be(NotificationStatus.Sent);
        notifications[0].SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailSendThrows_MarksNotificationFailed_NotSent()
    {
        var emailSender = Substitute.For<IEmailSender>();
        emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Formspree endpoint returned 404"));

        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(services => services.AddSingleton(emailSender));

        var eventId = Guid.NewGuid();
        var @event = MakeEvent(eventId);
        var payload = JsonSerializer.Serialize(@event, JsonOptions);

        var consumer = CreateConsumer(scopeFactory);
        await ProcessMessageAsync(consumer, payload);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notification = await db.Notifications.SingleAsync(n => n.SourceEventId == eventId);

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.SentAt.Should().BeNull();
    }
}
