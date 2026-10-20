using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("InvoiceLineItems");

        builder.HasKey(li => li.Id);

        builder.Property(li => li.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(li => li.Quantity).HasColumnType("numeric(18,2)");
        builder.Property(li => li.UnitPrice).HasColumnType("numeric(18,2)");
        builder.Property(li => li.LineTotal).HasColumnType("numeric(18,2)");

        builder.HasIndex(li => li.InvoiceId);
    }
}
