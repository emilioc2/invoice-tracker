using InvoiceTracker.Application.Common.Interfaces;

namespace InvoiceTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the production <c>KafkaEventPublisher</c> (a real <c>Confluent.Kafka</c> producer)
/// for the entire integration test suite — belt-and-braces alongside removing the three
/// Kafka-touching <c>IHostedService</c> registrations (see
/// <see cref="IntegrationTestWebApplicationFactory"/>): even though nothing should resolve
/// <see cref="IEventPublisher"/> once <c>OutboxRelayService</c> is removed, this guarantees no code
/// path can ever construct a real Kafka producer during a test run.
/// </summary>
public class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
