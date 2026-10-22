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
/// The subset of a successfully-authenticated identity's own state a caller needs after
/// <see cref="IIdentityService.ValidateCredentialsAsync"/> succeeds — resolved in that same call so
/// the Application layer never has to make a second round trip (or reference <c>ApplicationUser</c>
/// directly) just to read <c>Email</c>/<c>MustChangePassword</c> back off the identity it just
/// validated.
/// </summary>
public record AuthenticatedIdentityDto(Guid Id, string Email, bool MustChangePassword);

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
    /// correlated <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/> row's
    /// <c>UserId</c>. <paramref name="mustChangePassword"/> is set on the new identity at creation
    /// time — <c>true</c> for a Staff user created with an auto-generated temp password, <c>false</c>
    /// (the default) for a freshly-registered Owner who chooses their own password.
    /// </summary>
    Task<IdentityResultDto> CreateUserAsync(Guid userId, string email, string password, bool mustChangePassword = false);

    /// <summary>
    /// Returns the matching identity's Id/Email/MustChangePassword if <paramref name="email"/>/
    /// <paramref name="password"/> are a valid combination, otherwise null (no distinction made
    /// between "no such user" and "wrong password" — that's the caller's job to keep generic for
    /// security).
    /// </summary>
    Task<AuthenticatedIdentityDto?> ValidateCredentialsAsync(string email, string password);

    /// <summary>
    /// Resolves each requested identity's current email, keyed by Id — used wherever a
    /// <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/> needs to display its
    /// identity's email without that email being duplicated onto the membership row itself (see
    /// requirements.md's "Multi-business membership": email lives solely on the identity).
    /// Identities with no matching row (shouldn't happen in practice) are simply absent from the
    /// result rather than causing a failure.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetEmailsByUserIdsAsync(IEnumerable<Guid> userIds);

    /// <summary>
    /// Returns a password-reset token for the given email, or null if no account exists for it.
    /// Callers must not leak which of those two happened in their response.
    /// </summary>
    Task<string?> GeneratePasswordResetTokenAsync(string email);

    Task<IdentityResultDto> ResetPasswordAsync(string email, string token, string newPassword);

    /// <summary>
    /// Mid-session password change for a caller who knows their current password (as opposed to
    /// <see cref="ResetPasswordAsync"/>'s reset-token flow). Verifies
    /// <paramref name="currentPassword"/> against the stored hash first — a mismatch fails here,
    /// with no state changed, rather than falling through to <c>UserManager.ChangePasswordAsync</c>'s
    /// own (equivalent) check, so the Application layer gets one unambiguous failure path. On
    /// success, also clears the identity's <c>MustChangePassword</c> flag back to <c>false</c> — see
    /// architecture.md's "Forced password change for temp-password logins". Returns a failure (with
    /// a message callers should surface as a 400) if no credential-store user exists for
    /// <paramref name="userId"/>, if <paramref name="currentPassword"/> doesn't match, or if the
    /// underlying Identity operation otherwise rejects <paramref name="newPassword"/>.
    /// </summary>
    Task<IdentityResultDto> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);
}
