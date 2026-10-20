namespace InvoiceTracker.Application.Common.Interfaces;

/// <summary>
/// Exposes the authenticated caller's identity, resolved server-side from the JWT's claims.
/// Handlers must always scope their queries/commands by <see cref="BusinessId"/> from here —
/// never from a client-supplied value — per the project's multi-tenancy rule.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    Guid? BusinessId { get; }

    string? Role { get; }

    string? Email { get; }
}
