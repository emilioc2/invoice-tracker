using InvoiceTracker.Application.BusinessProfile.Common;
using MediatR;

namespace InvoiceTracker.Application.BusinessProfile.Commands.UpdatePaymentDetails;

/// <summary>
/// Updates <c>Business.PaymentDetails</c> for the caller's own business (resolved server-side via
/// <c>ICurrentUserService.BusinessId</c>, never a client-supplied id). <see cref="PaymentDetails"/>
/// is nullable: submitting <c>null</c> explicitly clears the business's default back to nothing,
/// per architecture.md's "Business payment details". Reachable by any authenticated user in the
/// business (Owner or Staff) — no role check.
/// </summary>
public record UpdatePaymentDetailsCommand(string? PaymentDetails) : IRequest<BusinessProfileDto>;
