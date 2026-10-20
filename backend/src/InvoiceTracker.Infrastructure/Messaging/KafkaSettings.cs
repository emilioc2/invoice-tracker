namespace InvoiceTracker.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Kafka" configuration section (see appsettings.Development.json). Single-broker
/// KRaft mode, matching the `kafka` service in the repo's docker-compose.yml.
/// </summary>
public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string OverdueTopic { get; set; } = "invoice-overdue";

    public string ConsumerGroupId { get; set; } = "invoice-tracker-overdue-consumer";
}
