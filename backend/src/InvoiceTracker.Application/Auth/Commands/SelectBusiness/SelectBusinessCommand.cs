using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.SelectBusiness;

/// <summary>
/// Completes a login that returned <c>RequiresBusinessSelection: true</c> (see
/// requirements.md's "Multi-business membership"). <see cref="PreAuthToken"/> is the short-lived
/// token <c>LoginCommandHandler</c> handed back — it carries only the identity's UserId, never a
/// business/role, and is re-validated server-side here (never trusted as-is). <see cref="BusinessId"/>
/// is the caller's chosen business from that login response's <c>memberships</c> list; it is still
/// re-checked here against the identity's actual active memberships rather than trusted outright.
/// </summary>
public record SelectBusinessCommand(string PreAuthToken, Guid BusinessId) : IRequest<AuthResultDto>;
