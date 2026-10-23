using InvoiceTracker.Application.BusinessProfile.Common;
using MediatR;

namespace InvoiceTracker.Application.BusinessProfile.Queries.GetBusinessProfile;

/// <summary>
/// Returns the caller's own business profile (<c>Id, Name, Email, PaymentDetails</c>) — resolved
/// server-side from <c>ICurrentUserService.BusinessId</c>, never a client-supplied id. Any
/// authenticated user may call this (Owner or Staff) — no role check, per
/// requirements.md's "Business payment details" section and architecture.md's REST API routes
/// table.
/// </summary>
public record GetBusinessProfileQuery : IRequest<BusinessProfileDto>;
