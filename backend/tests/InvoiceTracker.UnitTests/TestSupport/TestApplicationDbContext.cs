using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvoiceTracker.UnitTests.TestSupport;

/// <summary>
/// A minimal, test-only <see cref="IApplicationDbContext"/> implementation backed by EF Core's
/// in-memory provider, per architecture.md's test-plan guidance to exercise this kind of logic
/// "using EF Core's in-memory provider ... rather than mocking DbContext directly."
///
/// The in-memory provider doesn't support real relational transactions (<c>Database.BeginTransactionAsync</c>
/// throws against it), so <see cref="BeginTransactionAsync"/> here returns a no-op
/// <see cref="IDbContextTransaction"/> instead — the production <c>ApplicationDbContext</c> still
/// delegates to a real Postgres transaction; this fake only needs to satisfy the interface contract
/// so the code under test (which calls BeginTransactionAsync/CommitAsync) runs unmodified.
/// </summary>
public class TestApplicationDbContext : DbContext, IApplicationDbContext
{
    public TestApplicationDbContext(DbContextOptions<TestApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<User> BusinessUsers => Set<User>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<BusinessInvoiceCounter> BusinessInvoiceCounters => Set<BusinessInvoiceCounter>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IDbContextTransaction>(new NoOpDbContextTransaction());
    }

    public Task<int> GetNextInvoiceNumberAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Not needed by the Kafka pipeline unit tests this fake context was built for.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BusinessInvoiceCounter>().HasKey(c => c.BusinessId);
        modelBuilder.Entity<Invoice>().HasMany(i => i.LineItems).WithOne(li => li.Invoice!).HasForeignKey(li => li.InvoiceId);
    }
}

/// <summary>
/// No-op stand-in for <see cref="IDbContextTransaction"/> used only by <see cref="TestApplicationDbContext"/>
/// — EF Core's in-memory provider has no real transaction to begin/commit/roll back.
/// </summary>
internal sealed class NoOpDbContextTransaction : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();

    public void Commit()
    {
    }

    public void Rollback()
    {
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
