using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using InvoiceTracker.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvoiceTracker.Infrastructure.Persistence;

/// <summary>
/// Inherits IdentityDbContext so EF Core also manages the AspNetUsers/AspNetRoles/... tables for
/// the <see cref="ApplicationUser"/> credential store, alongside this project's own Domain
/// entity tables (Businesses, UserBusinessMemberships, Clients, Invoices, ...) configured below.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Business> Businesses => Set<Business>();

    // Named "UserBusinessMemberships" (not "Users") — IdentityDbContext's own `Users` property
    // (DbSet<ApplicationUser>, the credential store) is a different, unrelated table from this
    // project's own tenant-scoped UserBusinessMembership entity, so there's no name clash to avoid
    // the way the former "BusinessUsers" name had to.
    public DbSet<UserBusinessMembership> UserBusinessMemberships => Set<UserBusinessMembership>();

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BusinessInvoiceCounter> BusinessInvoiceCounters => Set<BusinessInvoiceCounter>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> GetNextInvoiceNumberAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        // Single atomic upsert: bootstraps the business's first counter row (LastNumber = 1) on
        // its first-ever invoice, or increments the existing row on every call after — one round
        // trip, no separate "create the counter row at registration" step, and no app-level
        // row-locking code. Postgres's own ON CONFLICT handling serializes concurrent callers for
        // the same BusinessId, which is what keeps this race-safe under concurrent invoice
        // creation (see CreateInvoiceCommandHandler, which calls this inside the same transaction
        // as the invoice insert).
        var results = await Database.SqlQuery<int>(
            $"""
             INSERT INTO "BusinessInvoiceCounters" ("BusinessId", "LastNumber") VALUES ({businessId}, 1)
             ON CONFLICT ("BusinessId") DO UPDATE SET "LastNumber" = "BusinessInvoiceCounters"."LastNumber" + 1
             RETURNING "LastNumber"
             """).ToListAsync(cancellationToken);

        return results.Single();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // ApplicationUser's one extra property beyond the stock IdentityUser<Guid> shape — see
        // ApplicationUser's own doc comment for why MustChangePassword lives on the identity
        // rather than on UserBusinessMembership.
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.MustChangePassword)
                .IsRequired()
                .HasDefaultValue(false);
        });
    }
}
