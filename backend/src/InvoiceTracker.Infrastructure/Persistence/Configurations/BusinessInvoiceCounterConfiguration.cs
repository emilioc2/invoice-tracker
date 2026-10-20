using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class BusinessInvoiceCounterConfiguration : IEntityTypeConfiguration<BusinessInvoiceCounter>
{
    public void Configure(EntityTypeBuilder<BusinessInvoiceCounter> builder)
    {
        builder.ToTable("BusinessInvoiceCounters");

        // One row per business — BusinessId is both the key and the FK.
        builder.HasKey(c => c.BusinessId);

        builder.Property(c => c.LastNumber)
            .IsRequired()
            .HasDefaultValue(0);
    }
}
