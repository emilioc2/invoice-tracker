namespace InvoiceTracker.Domain.Entities;

/// <summary>
/// Transactional outbox row: written in the same database transaction as the business-data change
/// that needs to publish an event (e.g. an invoice flipping to Overdue), so the "fact changed" and
/// "an event needs to be published for it" writes commit (or roll back) atomically. A separate
/// relay (<c>OutboxRelayService</c>) polls for unprocessed rows and publishes them to Kafka —
/// nothing here ever calls the Kafka producer directly.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    // null = not yet published. Set the moment the relay successfully publishes this row to Kafka.
    public DateTime? ProcessedAt { get; set; }
}
