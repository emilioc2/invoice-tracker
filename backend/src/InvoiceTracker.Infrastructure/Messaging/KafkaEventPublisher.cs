using Confluent.Kafka;
using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Infrastructure.Messaging;

/// <summary>
/// Thin Kafka-backed adapter for <see cref="IEventPublisher"/>. Only ever called from
/// <c>OutboxRelayService</c> — never directly from a command/query handler — per this project's
/// transactional-outbox convention. The underlying <see cref="IProducer{TKey,TValue}"/> is
/// thread-safe and meant to be long-lived, so this is registered as a singleton and its one
/// producer instance reused for the life of the process.
/// </summary>
public class KafkaEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IOptions<KafkaSettings> options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default)
    {
        await _producer.ProduceAsync(
            topic,
            new Message<string, string> { Key = key, Value = payload },
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        // Give in-flight sends a chance to land before the process exits.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
