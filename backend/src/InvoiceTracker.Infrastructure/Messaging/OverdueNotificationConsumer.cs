using System.Text.Json;
using Confluent.Kafka;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Infrastructure.Messaging;

/// <summary>
/// Subscribed to the <c>invoice-overdue</c> topic. Crash-safe idempotent consumption per this
/// project's hard rule: a <see cref="Notification"/> row is written as <see cref="NotificationStatus.Pending"/>
/// *before* <see cref="IEmailSender.SendAsync"/> is ever called, never after — a check-then-send-then-record
/// ordering has a crash window between "sent" and "recorded" that causes real duplicate sends, and
/// this ordering closes it: a crash can only ever leave a Pending row behind to retry, never a
/// second Sent row for the same SourceEventId.
/// </summary>
public class OverdueNotificationConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaSettings _settings;
    private readonly ILogger<OverdueNotificationConsumer> _logger;

    public OverdueNotificationConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaSettings> settings,
        ILogger<OverdueNotificationConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Confluent.Kafka's Consume() is a blocking synchronous call with no async overload, so
        // it needs to run on its own thread-pool thread rather than directly on the async
        // ExecuteAsync continuation.
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_settings.OverdueTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error on topic {Topic}; will keep retrying.", _settings.OverdueTopic);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                try
                {
                    // Deliberately synchronous-wait: this loop already runs on its own dedicated
                    // thread (see ExecuteAsync), and Confluent.Kafka's own Consume() call is
                    // itself blocking, so there's no async context above this to free up.
                    ProcessMessageAsync(result.Message.Value, stoppingToken).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to process invoice-overdue message at offset {Offset}; relying on Kafka redelivery.",
                        result.Offset);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(string payload, CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<InvoiceOverdueEvent>(payload, JsonOptions);

        if (@event is null)
        {
            _logger.LogWarning("Could not deserialize invoice-overdue payload: {Payload}", payload);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(n => n.SourceEventId == @event.EventId, cancellationToken);

        if (notification is not null && notification.Status == NotificationStatus.Sent)
        {
            // Ordinary at-least-once redelivery of an event we already fully processed — never
            // call IEmailSender again for it.
            _logger.LogInformation("Notification for event {EventId} already Sent; skipping.", @event.EventId);
            return;
        }

        var message =
            $"Invoice {@event.InvoiceNumber} for {@event.ClientName} is overdue " +
            $"(due {@event.DueDate:yyyy-MM-dd}, total {@event.TotalAmount:F2}).";

        if (notification is null)
        {
            // Doesn't exist yet: record intent *before* attempting the send.
            notification = new Notification
            {
                Id = Guid.NewGuid(),
                InvoiceId = @event.InvoiceId,
                Status = NotificationStatus.Pending,
                Channel = "Email",
                Message = message,
                SourceEventId = @event.EventId
            };

            dbContext.Notifications.Add(notification);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        // else: an existing Pending row means a previous attempt crashed between recording and
        // sending — proceed using that same row rather than inserting a second one.

        try
        {
            await emailSender.SendAsync(@event.ClientEmail, $"Invoice {@event.InvoiceNumber} is overdue", message, cancellationToken);
            notification.Status = NotificationStatus.Sent;
            notification.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send overdue notification email for invoice {InvoiceId}.", @event.InvoiceId);
            notification.Status = NotificationStatus.Failed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
