using InvoiceTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Narrow, Application-facing view of the EF Core <c>ApplicationDbContext</c> (which lives in
/// Infrastructure, alongside EF Core itself). Exposing this interface — rather than the concrete
/// DbContext type — keeps the inward-only Clean Architecture dependency rule intact while still
/// letting handlers query/persist domain entities and, where needed (e.g. Register's
/// multi-row-plus-Identity sequence), control a transaction explicitly.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Business> Businesses { get; }

    /// <summary>
    /// The tenant-scoped <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/> rows
    /// — one per (identity, business) pair. The underlying DbContext also inherits ASP.NET Core
    /// Identity's own <c>Users</c> property for the unrelated <c>ApplicationUser</c>
    /// credential-store rows, but "UserBusinessMemberships" doesn't clash with that name the way
    /// the former "Users"/"BusinessUsers" pairing did.
    /// </summary>
    DbSet<UserBusinessMembership> UserBusinessMemberships { get; }

    DbSet<Client> Clients { get; }

    DbSet<Invoice> Invoices { get; }

    DbSet<InvoiceLineItem> InvoiceLineItems { get; }

    DbSet<BusinessInvoiceCounter> BusinessInvoiceCounters { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically bootstraps (first call for a business) or increments (every call after) that
    /// business's <see cref="BusinessInvoiceCounter"/> row via a single `INSERT ... ON CONFLICT DO
    /// UPDATE ... RETURNING` statement, and returns the resulting number. Race-safe under
    /// concurrent callers for the same business — Postgres's own `ON CONFLICT` row-level locking
    /// does the work, with no separate SELECT-then-UPDATE round trip for a concurrent request to
    /// interleave with. Must be called inside the same transaction as the invoice insert it backs,
    /// so numbering and invoice creation commit (or roll back) together.
    /// </summary>
    Task<int> GetNextInvoiceNumberAsync(Guid businessId, CancellationToken cancellationToken = default);
}
