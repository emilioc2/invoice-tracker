namespace InvoiceTracker.Domain.Common;

/// <summary>
/// Marker interface for entities that belong to exactly one <c>Business</c> (the multi-tenancy
/// unit) and are looked up by their own <c>Id</c> scoped to that owning <c>BusinessId</c> — the
/// shape the Application layer's shared tenant-scoped lookup helper
/// (<c>TenantScopedQueryExtensions</c>) is generic over. See architecture.md's "Multi-tenancy &amp;
/// authorization" section.
/// </summary>
public interface ITenantScopedEntity
{
    Guid Id { get; }
    Guid BusinessId { get; }
}
