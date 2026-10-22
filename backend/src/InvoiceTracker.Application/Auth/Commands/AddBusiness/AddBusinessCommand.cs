using InvoiceTracker.Application.Auth.Common;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.AddBusiness;

/// <summary>
/// "Add a business" (requirements.md's "Multi-business membership"): requires a valid JWT. Creates
/// a new Business + a new Owner <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/>
/// for the caller's *existing* identity — no new identity/password is created. UserId is resolved
/// server-side from <c>ICurrentUserService</c>, never supplied by the caller. Behaves like
/// <see cref="InvoiceTracker.Application.Auth.Commands.SwitchBusiness.SwitchBusinessCommand"/> into
/// the new business afterward — the returned token is already scoped to it.
/// </summary>
public record AddBusinessCommand(string BusinessName, string BusinessEmail) : IRequest<AuthResultDto>;
