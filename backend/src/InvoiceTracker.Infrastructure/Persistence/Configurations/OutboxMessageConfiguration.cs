using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Payload)
            .IsRequired();

        builder.Property(m => m.OccurredAt)
            .IsRequired();

        // Hit by OutboxRelayService's polling query every tick.
        builder.HasIndex(m => m.ProcessedAt);
    }
}
