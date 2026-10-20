namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Outcome of an Identity operation (create user, reset password, ...), translated away from
/// ASP.NET Core Identity's own <c>IdentityResult</c> type so the Application layer never has to
/// reference Identity (or <c>ApplicationUser</c>, which lives in Infrastructure) directly.
/// </summary>
public record IdentityResultDto(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static IdentityResultDto Success() => new(true, Array.Empty<string>());

    public static IdentityResultDto Failure(IEnumerable<string> errors) => new(false, errors.ToArray());
}

/// <summary>
/// Port over ASP.NET Core Identity's credential store (UserManager/SignInManager), keeping the
/// concrete <c>ApplicationUser</c>/Identity types out of the Application layer per Clean
/// Architecture's inward-only dependency rule. The Infrastructure-side implementation is the
/// only place that knows about <c>ApplicationUser</c>.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Creates the credential-store row. <paramref name="userId"/> must be the same Guid as the
    /// correlated <see cref="InvoiceTracker.Domain.Entities.User"/> row's Id.
    /// </summary>
    Task<IdentityResultDto> CreateUserAsync(Guid userId, string email, string password);

    /// <summary>
    /// Returns the matching user's Id if <paramref name="email"/>/<paramref name="password"/> are
    /// a valid combination, otherwise null (no distinction made between "no such user" and
    /// "wrong password" — that's the caller's job to keep generic for security).
    /// </summary>
    Task<Guid?> ValidateCredentialsAsync(string email, string password);

    /// <summary>
    /// Returns a password-reset token for the given email, or null if no account exists for it.
    /// Callers must not leak which of those two happened in their response.
    /// </summary>
    Task<string?> GeneratePasswordResetTokenAsync(string email);

    Task<IdentityResultDto> ResetPasswordAsync(string email, string token, string newPassword);
}
