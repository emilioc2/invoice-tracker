using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InvoiceTracker.Infrastructure.Identity;

/// <summary>
/// The only place in the codebase that touches <see cref="ApplicationUser"/>/<see cref="UserManager{TUser}"/>
/// directly — everything above the Infrastructure layer depends only on <see cref="IIdentityService"/>.
/// </summary>
public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentityService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<IdentityResultDto> CreateUserAsync(Guid userId, string email, string password, bool mustChangePassword = false)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            MustChangePassword = mustChangePassword
        };

        var result = await _userManager.CreateAsync(user, password);

        return result.Succeeded
            ? IdentityResultDto.Success()
            : IdentityResultDto.Failure(result.Errors.Select(e => e.Description));
    }

    public async Task<AuthenticatedIdentityDto?> ValidateCredentialsAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        var isValidPassword = await _userManager.CheckPasswordAsync(user, password);
        return isValidPassword
            ? new AuthenticatedIdentityDto(user.Id, user.Email ?? email, user.MustChangePassword)
            : null;
    }

    public async Task<Guid?> FindUserIdByEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return user?.Id;
    }

    public async Task<AuthenticatedIdentityDto?> FindIdentityByIdAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : new AuthenticatedIdentityDto(user.Id, user.Email ?? string.Empty, user.MustChangePassword);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(IEnumerable<Guid> userIds)
    {
        var idSet = userIds.ToHashSet();
        if (idSet.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _userManager.Users
            .Where(u => idSet.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty);
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return user is null ? null : await _userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<IdentityResultDto> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Deliberately generic — matches ASP.NET Core Identity's own "invalid token" failure
            // shape, so this doesn't leak whether the email exists either.
            return IdentityResultDto.Failure(new[] { "Invalid token." });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        return result.Succeeded
            ? IdentityResultDto.Success()
            : IdentityResultDto.Failure(result.Errors.Select(e => e.Description));
    }

    public async Task<IdentityResultDto> SetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return IdentityResultDto.Failure(new[] { "User not found." });
        }

        // No current-password check: the caller already holds a valid JWT issued moments earlier
        // at the login that used the (temp) password — see this method's own doc comment on
        // IIdentityService. RemovePasswordAsync + AddPasswordAsync is used instead of
        // UserManager.ChangePasswordAsync specifically because the latter requires the old password.
        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
        {
            return IdentityResultDto.Failure(removeResult.Errors.Select(e => e.Description));
        }

        var addResult = await _userManager.AddPasswordAsync(user, newPassword);
        if (!addResult.Succeeded)
        {
            return IdentityResultDto.Failure(addResult.Errors.Select(e => e.Description));
        }

        // Forced password change (see architecture.md's "Forced password change for temp-password
        // logins"): clear the flag on a successful change, whether or not it was set — this is the
        // only place MustChangePassword lives now that it's on ApplicationUser rather than a Domain
        // User/membership row.
        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
        }

        return IdentityResultDto.Success();
    }
}
