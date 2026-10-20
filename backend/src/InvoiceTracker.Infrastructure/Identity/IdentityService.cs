using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

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

    public async Task<IdentityResultDto> CreateUserAsync(Guid userId, string email, string password)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email,
            Email = email
        };

        var result = await _userManager.CreateAsync(user, password);

        return result.Succeeded
            ? IdentityResultDto.Success()
            : IdentityResultDto.Failure(result.Errors.Select(e => e.Description));
    }

    public async Task<Guid?> ValidateCredentialsAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        var isValidPassword = await _userManager.CheckPasswordAsync(user, password);
        return isValidPassword ? user.Id : null;
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
}
