namespace InvoiceTracker.Application.Auth.Common;

/// <summary>
/// Shared success shape for Register and Login — both end with a usable JWT.
/// </summary>
public record AuthResultDto(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid UserId,
    Guid BusinessId,
    string Email,
    string Role);
