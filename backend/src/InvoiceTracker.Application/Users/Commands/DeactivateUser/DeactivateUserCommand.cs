using MediatR;

namespace InvoiceTracker.Application.Users.Commands.DeactivateUser;

/// <summary>
/// Owner-only: deactivates (transitions to <c>UserStatus.Deactivated</c>) a user in the caller's business.
/// Never a hard delete. See <see cref="DeactivateUserCommandHandler"/> for the rejection rules.
/// </summary>
public record DeactivateUserCommand(Guid Id) : IRequest;
