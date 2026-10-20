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
/// entity tables (Businesses, Users, Clients, Invoices, ...) configured below.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Business> Businesses => Set<Business>();

    // Named "BusinessUsers" (not "Users") to avoid clashing with IdentityDbContext's own
    // `Users` property, which is the DbSet<ApplicationUser> credential store — a different,
    // unrelated table from this project's own tenant-scoped Domain User entity.
    public DbSet<User> BusinessUsers => Set<User>();

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BusinessInvoiceCounter> BusinessInvoiceCounters => Set<BusinessInvoiceCounter>();

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await Database.BeginTransactionAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
