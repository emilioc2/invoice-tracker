namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Result of issuing a JWT: the encoded token plus its expiry, so callers (and the controller)
/// know when the frontend should expect to be forced back to re-login.
/// </summary>
public record AuthToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Result of issuing a short-lived preauth token (see requirements.md's "Multi-business
/// membership"/architecture.md's Auth routes): carries only <c>sub</c> = UserId and a
/// <c>"preauth": "true"</c> claim, no <c>businessId</c>/<c>role</c>. Its only valid use is being
/// handed back to <c>POST /api/auth/select-business</c> — the normal JWT bearer pipeline rejects
/// any token carrying the <c>preauth</c> claim for every other endpoint (see
/// <c>DependencyInjection.AddJwtBearer</c>'s <c>OnTokenValidated</c> event).
/// </summary>
public record PreAuthToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Issues a signed JWT carrying the claims the rest of the API relies on for multi-tenancy
/// and role checks: UserId (NameIdentifier), BusinessId, Role, Email.
/// </summary>
public interface IJwtTokenGenerator
{
    AuthToken GenerateToken(Guid userId, Guid businessId, string role, string email);

    /// <summary>Issues a short-lived (~5 minute) preauth token for a login that matched more than one active membership.</summary>
    PreAuthToken GeneratePreAuthToken(Guid userId);

    /// <summary>
    /// Validates <paramref name="token"/>'s signature/expiry and that it genuinely carries the
    /// <c>preauth</c> claim (not a normal access token), returning the carried UserId if so, or
    /// null if the token is invalid, expired, or not a preauth token. Used only by
    /// <c>SelectBusinessCommandHandler</c> — this is the one place a preauth token is ever accepted.
    /// </summary>
    Guid? ValidatePreAuthToken(string token);
}
