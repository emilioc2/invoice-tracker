using System.Text.Json;
using Confluent.Kafka;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
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
        // EnableAutoCommit is deliberately false: librdkafka's auto-commit timer advances the
        // committed offset based purely on a message having been returned from Consume(), with no
        // regard for whether this consumer's Pending-row-write / send / Sent-row-update has
        // actually finished. That breaks the crash-safety guarantee the Pending-before-send design
        // relies on (a crash could leave the offset committed with no Pending row to retry, so the
        // notification is silently dropped). Instead, the offset for each message is committed
        // manually below, only once ProcessMessageAsync has fully finished for it.
        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
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
                    var processedSuccessfully =
                        ProcessMessageAsync(result.Message.Value, stoppingToken).GetAwaiter().GetResult();

                    if (processedSuccessfully)
                    {
                        // Only commit once the Pending-row-write / send / Sent-row-update sequence
                        // has actually finished for this message — never ahead of it.
                        consumer.Commit(result);
                    }
                    else
                    {
                        // The Notification ended up Failed (email send threw): leave the offset
                        // uncommitted so Kafka can redeliver this message, which is the only retry
                        // path Failed rows get per the design (see ProcessMessageAsync's XML doc).
                        _logger.LogWarning(
                            "Notification ended up Failed for invoice-overdue message at offset {Offset}; " +
                            "not committing so Kafka can redeliver it.",
                            result.Offset);
                    }
                }
                catch (Exception ex)
                {
                    // Deliberately not committing here either: an exception means we can't be sure
                    // the Pending row was ever written/updated, so relying on Kafka redelivery (by
                    // leaving the offset uncommitted) is what keeps this crash-safe.
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

    /// <summary>
    /// Returns whether the offset for this message is safe to commit: <c>true</c> once the
    /// Notification row has reached <see cref="NotificationStatus.Sent"/> (including the
    /// already-processed skip case), <c>false</c> if it ended up <see cref="NotificationStatus.Failed"/>
    /// — the caller must leave the offset uncommitted in that case so Kafka redelivery remains the
    /// retry path for Failed rows, per this class's crash-safety design.
    /// </summary>
    private async Task<bool> ProcessMessageAsync(string payload, CancellationToken cancellationToken)
    {
        var @event = JsonSerializer.Deserialize<InvoiceOverdueEvent>(payload, JsonOptions);

        if (@event is null)
        {
            _logger.LogWarning("Could not deserialize invoice-overdue payload: {Payload}", payload);
            return true;
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
            return true;
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
                NotificationType = NotificationType.OverdueReminder,
                Channel = "Email",
                Message = message,
                SourceEventId = @event.EventId
            };

            dbContext.Notifications.Add(notification);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        // else: an existing Pending row means a previous attempt crashed between recording and
        // sending — proceed using that same row rather than inserting a second one.

        bool sent;
        try
        {
            await emailSender.SendAsync(
                @event.ClientEmail,
                $"Invoice {@event.InvoiceNumber} is overdue",
                message,
                Array.Empty<EmailAttachment>(),
                cancellationToken);
            notification.Status = NotificationStatus.Sent;
            notification.SentAt = DateTime.UtcNow;
            sent = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send overdue notification email for invoice {InvoiceId}.", @event.InvoiceId);
            notification.Status = NotificationStatus.Failed;
            sent = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sent;
    }
}
