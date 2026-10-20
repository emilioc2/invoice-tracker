using InvoiceTracker.Application.Users.Common;
using MediatR;

namespace InvoiceTracker.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Owner-only: creates a Staff user for the caller's business (requirements.md's "Multi-user
/// onboarding" — no separate invite-token flow, just email + an auto-generated temp password
/// emailed via <see cref="InvoiceTracker.Application.Common.Interfaces.IEmailSender"/>).
/// BusinessId is never part of this command's shape — it's resolved server-side from the JWT
/// inside the handler via <c>ICurrentUserService</c>, never supplied by the caller.
/// </summary>
public record CreateStaffUserCommand(string Email) : IRequest<UserDto>;
