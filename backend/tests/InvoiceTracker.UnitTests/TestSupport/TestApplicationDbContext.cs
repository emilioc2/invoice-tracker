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
    public DbSet<UserBusinessMembership> UserBusinessMemberships => Set<UserBusinessMembership>();
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

    /// <summary>
    /// The production implementation (<c>ApplicationDbContext.GetNextInvoiceNumberAsync</c>) uses a
    /// Postgres-specific raw-SQL atomic upsert (<c>INSERT ... ON CONFLICT ... RETURNING</c>) to stay
    /// race-safe under concurrent invoice creation — EF Core's in-memory provider doesn't support
    /// that SQL, and per architecture.md's test plan, concurrency-safety itself is integration-test
    /// territory, not unit. This fake re-implements just the per-business sequencing semantics
    /// (first call for a business starts at 1, each subsequent call increments by 1) via plain LINQ
    /// so handler-level unit tests (e.g. <c>CreateInvoiceCommandHandler</c>'s numbering/formatting
    /// logic) can exercise the "INV-0001 -> INV-0002" behavior without needing a real Postgres.
    /// </summary>
    public async Task<int> GetNextInvoiceNumberAsync(Guid businessId, CancellationToken cancellationToken = default)
    {
        var counter = await BusinessInvoiceCounters.SingleOrDefaultAsync(c => c.BusinessId == businessId, cancellationToken);
        if (counter is null)
        {
            counter = new BusinessInvoiceCounter { BusinessId = businessId, LastNumber = 1 };
            BusinessInvoiceCounters.Add(counter);
        }
        else
        {
            counter.LastNumber += 1;
        }

        await SaveChangesAsync(cancellationToken);
        return counter.LastNumber;
    }

    /// <summary>
    /// Convenience factory for tests that just need a standalone in-memory context (handler tests
    /// that construct their own <see cref="IApplicationDbContext"/> directly, as opposed to the
    /// Kafka BackgroundServices tests which go through <see cref="ScopeFactoryHelper"/>).
    /// </summary>
    public static TestApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestApplicationDbContext(options);
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
