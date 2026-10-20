using FluentAssertions;
using InvoiceTracker.Api.BackgroundServices;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceTracker.UnitTests.Kafka;

/// <summary>
/// Unit tests for <see cref="OutboxRelayService"/> per architecture.md's test plan: publishes
/// unprocessed outbox rows via a mocked <see cref="IEventPublisher"/> (no real Kafka broker),
/// marks them processed only on confirmed success, and leaves a failed publish for the next tick
/// to retry.
/// </summary>
public class OutboxRelayServiceTests
{
    private static OutboxRelayService CreateRelay(IServiceScopeFactory scopeFactory)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new OutboxRelayService(scopeFactory, configuration, NullLogger<OutboxRelayService>.Instance);
    }

    private static async Task RelayAsync(OutboxRelayService relay)
    {
        await PrivateMethodInvoker.InvokeAsync(relay, "RelayAsync", CancellationToken.None);
    }

    [Fact]
    public async Task UnprocessedOutboxRow_IsPublished_KeyedByInvoiceId_AndMarkedProcessed()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(
            services => services.AddSingleton(publisher));

        var invoiceId = Guid.NewGuid();
        var payload = $$"""{"eventId":"{{Guid.NewGuid()}}","invoiceId":"{{invoiceId}}"}""";
        Guid messageId;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "InvoiceOverdue",
                Payload = payload,
                OccurredAt = DateTime.UtcNow,
                ProcessedAt = null
            };
            messageId = message.Id;
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }

        var relay = CreateRelay(scopeFactory);
        await RelayAsync(relay);

        await publisher.Received(1).PublishAsync(
            "invoice-overdue", invoiceId.ToString(), payload, Arg.Any<CancellationToken>());

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.OutboxMessages.SingleAsync(m => m.Id == messageId);
        reloaded.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AlreadyProcessedOutboxRow_IsNeverRePublished()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(
            services => services.AddSingleton(publisher));

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "InvoiceOverdue",
                Payload = $$"""{"invoiceId":"{{Guid.NewGuid()}}"}""",
                OccurredAt = DateTime.UtcNow.AddMinutes(-5),
                ProcessedAt = DateTime.UtcNow.AddMinutes(-4)
            });
            await db.SaveChangesAsync();
        }

        var relay = CreateRelay(scopeFactory);
        await RelayAsync(relay);

        await publisher.DidNotReceive().PublishAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedPublish_LeavesRowUnprocessed_ForRetryOnNextTick()
    {
        var publisher = Substitute.For<IEventPublisher>();
        publisher
            .PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Kafka broker unavailable"));

        var scopeFactory = ScopeFactoryHelper.BuildScopeFactory(
            services => services.AddSingleton(publisher));

        var invoiceId = Guid.NewGuid();
        Guid messageId;

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "InvoiceOverdue",
                Payload = $$"""{"invoiceId":"{{invoiceId}}"}""",
                OccurredAt = DateTime.UtcNow,
                ProcessedAt = null
            };
            messageId = message.Id;
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }

        var relay = CreateRelay(scopeFactory);

        // Must not throw out of RelayAsync — a publish failure is swallowed/logged so the rest of
        // the batch (and the next tick) still get a chance.
        var act = async () => await RelayAsync(relay);
        await act.Should().NotThrowAsync();

        using var verifyScope = scopeFactory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var reloaded = await verifyDb.OutboxMessages.SingleAsync(m => m.Id == messageId);
        reloaded.ProcessedAt.Should().BeNull();
    }
}
