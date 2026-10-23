using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Resolves the authenticated caller via <see cref="ICurrentUserService"/> (never a
/// client-supplied id — same pattern as every other authenticated handler) and sets the
/// credential-store password through <see cref="IIdentityService.SetPasswordAsync"/>, with no
/// current-password verification — the caller's JWT, issued moments earlier at the login that
/// used the temp password, is proof enough (see architecture.md's "Forced password change for
/// temp-password logins"). On success, <see cref="IIdentityService.SetPasswordAsync"/> also
/// clears <c>ApplicationUser.MustChangePassword</c> back to false (see <see cref="IIdentityService"/>'s
/// own doc comment: that flag lives on the identity now, not on any one
/// <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/> row, so there is nothing left
/// for this handler itself to update once the Identity operation succeeds). Any rejection from the
/// underlying Identity operation (e.g. a password policy failure) fails with no state changed.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;

    public ChangePasswordCommandHandler(IIdentityService identityService, ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _currentUserService = currentUserService;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;

        var result = await _identityService.SetPasswordAsync(userId, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new BadRequestException(string.Join("; ", result.Errors));
        }

        return Unit.Value;
    }
}
