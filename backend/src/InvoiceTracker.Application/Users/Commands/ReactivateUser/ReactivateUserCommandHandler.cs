using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Users.Commands.ReactivateUser;

/// <summary>
/// Mirror of <see cref="InvoiceTracker.Application.Users.Commands.DeactivateUser.DeactivateUserCommandHandler"/>,
/// same tenant-scoping/role-check pattern:
/// 1. Target doesn't exist, or belongs to a different business than the caller's — 404, same
///    cross-tenant rule as Clients/Invoices.
/// 2. Target's Role is Owner (defensive — an Owner can never be deactivated in the first place,
///    so this should be unreachable, but isn't relied upon) — 400.
/// Unlike deactivate, there's no "can't target yourself" check: an Owner can never deactivate
/// their own account, so an Owner's own Status is always Active and this path never applies to
/// the caller themself.
/// Reactivating an already-active user is idempotent: a no-op success, not an error, matching the
/// status-idempotency pattern used elsewhere.
/// </summary>
public class ReactivateUserCommandHandler : IRequestHandler<ReactivateUserCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public ReactivateUserCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(ReactivateUserCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;

        var targetUser = await _dbContext.UserBusinessMemberships
            .GetForBusinessOrThrowAsync(request.Id, businessId, "User not found.", cancellationToken);

        if (targetUser.Role == UserRole.Owner)
        {
            throw new BadRequestException("Cannot reactivate an Owner account.");
        }

        if (targetUser.Status == UserStatus.Active)
        {
            return;
        }

        targetUser.TransitionTo(UserStatus.Active);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
