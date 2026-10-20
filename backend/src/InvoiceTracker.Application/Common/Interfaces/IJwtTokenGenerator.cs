namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Result of issuing a JWT: the encoded token plus its expiry, so callers (and the controller)
/// know when the frontend should expect to be forced back to re-login.
/// </summary>
public record AuthToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Issues a signed JWT carrying the claims the rest of the API relies on for multi-tenancy
/// and role checks: UserId (NameIdentifier), BusinessId, Role, Email.
/// </summary>
public interface IJwtTokenGenerator
{
    AuthToken GenerateToken(Guid userId, Guid businessId, string role, string email);
}
