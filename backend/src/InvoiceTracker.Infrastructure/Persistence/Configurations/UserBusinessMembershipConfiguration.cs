using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InvoiceTracker.Infrastructure.Persistence.Configurations;

public class UserBusinessMembershipConfiguration : IEntityTypeConfiguration<UserBusinessMembership>
{
    public void Configure(EntityTypeBuilder<UserBusinessMembership> builder)
    {
        builder.ToTable("UserBusinessMemberships");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(m => m.StatusChangedAt)
            .IsRequired();

        // One membership per (identity, business) pair — the same identity can hold a second
        // membership in a different business, but never two in the same one.
        builder.HasIndex(m => new { m.UserId, m.BusinessId }).IsUnique();

        // Hit by ListUsersQueryHandler-style "every membership in this business" queries.
        builder.HasIndex(m => m.BusinessId);

        // Hit by "every membership this identity holds" lookups (needed once multi-business
        // membership's login/select-business/switch-business flows land — see
        // requirements.md's "Multi-business membership").
        builder.HasIndex(m => m.UserId);
    }
}
