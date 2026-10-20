namespace InvoiceTracker.Domain.Events;

/// <summary>
/// The domain-side shape of the Kafka <c>invoice-overdue</c> topic's payload (see
/// architecture.md's "Kafka: overdue-notification pipeline" event contract). Serialization is the
/// concern of whoever produces/consumes this (the Api-layer scanner/relay and the
/// Infrastructure-layer consumer), not this record itself — kept here as a plain POCO so Domain
/// stays free of any serialization-library dependency.
///
/// Property names are PascalCase by .NET convention; producers/consumers serialize with a
/// camelCase naming policy to match the documented JSON contract (eventId, eventType, ...).
/// </summary>
public record InvoiceOverdueEvent(
    Guid EventId,
    string EventType,
    DateTime OccurredAt,
    Guid InvoiceId,
    Guid BusinessId,
    string InvoiceNumber,
    Guid ClientId,
    string ClientName,
    string ClientEmail,
    DateTime DueDate,
    decimal TotalAmount);
