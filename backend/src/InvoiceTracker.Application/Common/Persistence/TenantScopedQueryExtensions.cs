using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Common.Persistence;

/// <summary>
/// Shared "get by id, scoped to the caller's BusinessId, or 404" lookup used by the
/// GetXById/UpdateX/status-change handlers repeated across Clients, Invoices, and Users (see
/// architecture.md's "Multi-tenancy &amp; authorization" section). Centralizing this here means the
/// multi-tenancy filter is structurally the same everywhere it's used instead of each handler
/// restating <c>.FirstOrDefaultAsync(x => x.Id == id &amp;&amp; x.BusinessId == businessId)</c> plus its
/// own null-check-and-throw inline — directly serving the "never trust a client-supplied business
/// ID" rule: a record that exists but belongs to a different business is indistinguishable from
/// one that doesn't exist, so this always throws <see cref="NotFoundException"/> (404), never a
/// 403, for either case.
///
/// Callers chain any <c>Include</c>/<c>AsNoTracking</c> they need onto the <see cref="IQueryable{T}"/>
/// before calling this, so read-only queries and tracked-for-mutation queries both still work.
///
/// Deliberately excluded: <c>GetInvoicePdfQueryHandler</c>, which fetches Invoice/Client/Business
/// together via a single joined query with a different shape that doesn't fit this generic
/// single-entity helper.
/// </summary>
public static class TenantScopedQueryExtensions
{
    public static async Task<TEntity> GetForBusinessOrThrowAsync<TEntity>(
        this IQueryable<TEntity> query,
        Guid id,
        Guid businessId,
        string notFoundMessage,
        CancellationToken cancellationToken)
        where TEntity : class, ITenantScopedEntity
    {
        var entity = await query
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.BusinessId == businessId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException(notFoundMessage);
        }

        return entity;
    }
}
