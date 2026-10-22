using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Persistence;
using InvoiceTracker.Domain.Enums;
using MediatR;

namespace InvoiceTracker.Application.Users.Commands.DeactivateUser;

/// <summary>
/// Rejection rules, checked in this order (requirements.md's Permissions section +
/// CLAUDE.md's "User deactivation" convention):
/// 1. Target doesn't exist, or belongs to a different business than the caller's — 404, same
///    cross-tenant rule as Clients/Invoices. Checked first so a cross-tenant request can't learn
///    anything else about the target (e.g. that it's an Owner) via a different status code.
/// 2. Target is the caller themself — 400. An Owner can never deactivate their own account;
///    since there's exactly one Owner per business, this is what prevents a business from ever
///    losing all user-management access.
/// 3. Target's Role is Owner (even if not the caller — defensive; no code path should ever
///    produce a second Owner for a business, but this isn't relied upon) — 400.
/// Deactivating an already-inactive user is idempotent: a no-op success, not an error, matching
/// the archive-idempotency pattern used for Client/Invoice soft deletes.
/// </summary>
public class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public DeactivateUserCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var businessId = _currentUserService.BusinessId!.Value;
        var currentUserId = _currentUserService.UserId!.Value;

        var targetUser = await _dbContext.BusinessUsers
            .GetForBusinessOrThrowAsync(request.Id, businessId, "User not found.", cancellationToken);

        if (targetUser.Id == currentUserId)
        {
            throw new BadRequestException("An Owner cannot deactivate their own account.");
        }

        if (targetUser.Role == UserRole.Owner)
        {
            throw new BadRequestException("Cannot deactivate an Owner account.");
        }

        if (!targetUser.IsActive)
        {
            return;
        }

        targetUser.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
