namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Generic outbound event-publishing port. The concrete adapter (Kafka) lives in Infrastructure —
/// the Application layer (and, in this project, the Api-layer <c>OutboxRelayService</c>) only
/// depends on this shape. Never called directly from a command/query handler — only the
/// transactional-outbox relay publishes, per this project's Kafka reliability convention.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes <paramref name="payload"/> (already-serialized JSON) to <paramref name="topic"/>,
    /// keyed by <paramref name="key"/> (e.g. the InvoiceId, so all events for one invoice land on
    /// the same partition).
    /// </summary>
    Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default);
}
