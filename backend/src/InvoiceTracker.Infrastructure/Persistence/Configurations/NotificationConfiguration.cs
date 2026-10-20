using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(n => n.Channel)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.Message)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(n => n.InvoiceId);

        // Used for Kafka idempotency — a SourceEventId is processed at most once.
        builder.HasIndex(n => n.SourceEventId)
            .IsUnique();
    }
}
