using MediatR;

namespace InvoiceTracker.Application.Users.Commands.ReactivateUser;

/// <summary>
/// Owner-only: reactivates (transitions back to <c>UserStatus.Active</c>) a previously
/// deactivated Staff user in the caller's business, restoring their ability to log in. Mirror of
/// <see cref="InvoiceTracker.Application.Users.Commands.DeactivateUser.DeactivateUserCommand"/> —
/// see <see cref="ReactivateUserCommandHandler"/> for the rejection rules.
/// </summary>
public record ReactivateUserCommand(Guid Id) : IRequest;
