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
    /// The tenant-scoped <see cref="InvoiceTracker.Domain.Entities.User"/> rows. Named
    /// "BusinessUsers" (not "Users") because the underlying DbContext also inherits
    /// ASP.NET Core Identity's own <c>Users</c> property for the unrelated <c>ApplicationUser</c>
    /// credential-store rows — this avoids a property-name clash between the two.
    /// </summary>
    DbSet<User> BusinessUsers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
