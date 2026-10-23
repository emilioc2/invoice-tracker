namespace InvoiceTracker.Application.BusinessProfile.Common;

// Note on this namespace: architecture.md's file tree names this feature's folder
// "Application/Business/" (kept as-is on disk), but every file under it declares its C#
// namespace as "InvoiceTracker.Application.BusinessProfile", not "...Application.Business" —
// a deliberate, narrow deviation from this project's usual folder-equals-namespace convention.
// Every other feature folder (Clients, Invoices, Users, Notifications) is already pluralized
// specifically to avoid colliding with its singular Domain entity of the same name (Client,
// Invoice, UserBusinessMembership, Notification); "Business" has no natural plural folder name
// that reads well here, so a literal "InvoiceTracker.Application.Business" namespace would
// instead shadow the Domain "Business" entity type for every file project-wide that does
// `using InvoiceTracker.Domain.Entities;` from any namespace nested under
// "InvoiceTracker.Application" (confirmed by an actual build: it broke more than a dozen
// unrelated existing files with CS0118 "'Business' is a namespace but is used like a type").
// "BusinessProfile" sidesteps that without touching anything else in the codebase.

/// <summary>
/// The caller's own business profile, per architecture.md's REST API routes table:
/// <c>GET /api/business</c> returns exactly these four fields.
/// </summary>
public record BusinessProfileDto(
    Guid Id,
    string Name,
    string Email,
    string? PaymentDetails);
