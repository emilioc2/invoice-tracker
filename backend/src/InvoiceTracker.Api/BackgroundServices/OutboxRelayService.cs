using System.Text.Json;
using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Api.BackgroundServices;

/// <summary>
/// Polls OutboxMessages for unprocessed rows and publishes each to Kafka via <see cref="IEventPublisher"/>
/// (the only thing in this pipeline that actually talks to Kafka's producer side — see
/// Infrastructure/Messaging/KafkaEventPublisher.cs). Communicates with
/// <c>OverdueInvoiceScannerService</c> only via the OutboxMessages table, never by direct method
/// call. If Kafka is briefly unavailable, a failed publish is simply retried on the next tick —
/// the ProcessedAt column is only set after a confirmed successful publish, so nothing is lost,
/// only delayed.
/// </summary>
public class OutboxRelayService : BackgroundService
{
    private const string OverdueTopic = "invoice-overdue";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayService> _logger;
    private readonly TimeSpan _interval;

    public OutboxRelayService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var seconds = configuration.GetValue<int?>("Kafka:OutboxRelayIntervalSeconds") ?? 15;
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox relay pass failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RelayAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            try
            {
                var key = ExtractMessageKey(message.Payload) ?? message.Id.ToString();

                await publisher.PublishAsync(OverdueTopic, key, message.Payload, cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Leave ProcessedAt null so this row is picked up again next tick. Don't let one
                // failing publish stop the rest of this batch from being attempted.
                _logger.LogWarning(ex, "Failed to publish outbox message {MessageId}; will retry next tick.", message.Id);
            }
        }
    }

    /// <summary>
    /// Pulls the Kafka message key (InvoiceId, per the event contract) out of the payload JSON
    /// without needing to know the specific event-type shape — every event this pipeline
    /// currently produces carries an "invoiceId" field.
    /// </summary>
    private static string? ExtractMessageKey(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.TryGetProperty("invoiceId", out var invoiceIdProperty)
            ? invoiceIdProperty.GetString()
            : null;
    }
}
