using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Resolves the authenticated caller via <see cref="ICurrentUserService"/> (never a
/// client-supplied id — same pattern as every other authenticated handler), verifies
/// <see cref="ChangePasswordCommand.CurrentPassword"/> and changes the credential-store password
/// through <see cref="IIdentityService.ChangePasswordAsync"/>, then — on success — clears
/// <c>User.MustChangePassword</c> back to false. A wrong current password (or any other rejection
/// from the underlying Identity operation) fails with no state changed: ChangePasswordAsync
/// either changes the password and the caller then clears the flag, or nothing happens at all.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserService _currentUserService;

    public ChangePasswordCommandHandler(
        IApplicationDbContext dbContext,
        IIdentityService identityService,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _identityService = identityService;
        _currentUserService = currentUserService;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;

        var result = await _identityService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw new BadRequestException(string.Join("; ", result.Errors));
        }

        var user = await _dbContext.BusinessUsers
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is not null)
        {
            user.MustChangePassword = false;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
