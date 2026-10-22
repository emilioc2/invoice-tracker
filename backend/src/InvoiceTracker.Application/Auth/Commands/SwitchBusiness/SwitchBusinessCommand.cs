using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.SwitchBusiness;

/// <summary>
/// Requires a valid (normal, non-preauth) JWT. Issues a brand-new JWT scoped to a different one of
/// the caller's own active memberships, without re-entering a password — see requirements.md's
/// "Multi-business membership". <see cref="BusinessId"/> is only ever used to look up a membership
/// for the caller's own UserId (resolved server-side via <c>ICurrentUserService</c>), never trusted
/// outright — a request for a business the caller doesn't belong to is rejected with
/// <c>404</c>, same as every other cross-tenant lookup in this codebase. The previous token is not
/// revoked (unchanged accepted limitation — see requirements.md's Auth row).
/// </summary>
public record SwitchBusinessCommand(Guid BusinessId) : IRequest<AuthResultDto>;
