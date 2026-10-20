using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(i => i.Subtotal).HasColumnType("numeric(18,2)");
        builder.Property(i => i.TaxRate).HasColumnType("numeric(5,4)");
        builder.Property(i => i.TaxAmount).HasColumnType("numeric(18,2)");
        builder.Property(i => i.Total).HasColumnType("numeric(18,2)");

        builder.Property(i => i.Notes).HasMaxLength(2000);

        builder.Property(i => i.CreatedAtDate).IsRequired();
        builder.Property(i => i.DueDate).IsRequired();
        builder.Property(i => i.LastModifiedDate).IsRequired();

        builder.HasMany(i => i.LineItems)
            .WithOne(li => li.Invoice)
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Hit by the overdue scanner, the filtered invoice list endpoint, and
        // the dashboard summary.
        builder.HasIndex(i => new { i.BusinessId, i.Status, i.DueDate });

        // Hit by list queries that filter active/archived invoices per business.
        builder.HasIndex(i => new { i.BusinessId, i.ArchivedAt });

        // Invoice numbers are unique per business, not globally.
        builder.HasIndex(i => new { i.BusinessId, i.InvoiceNumber })
            .IsUnique();
    }
}
