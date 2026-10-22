using InvoiceTracker.Application.Common.Interfaces;

namespace InvoiceTracker.Application.Auth.Common;

/// <summary>
/// Shared success shape for Register, Login (single-membership case), SelectBusiness,
/// SwitchBusiness, and AddBusiness — all end with a usable JWT.
///
/// Login's multi-membership case (see requirements.md's "Multi-business membership") returns this
/// same DTO shape but with the token-bearing fields all null and <see cref="RequiresBusinessSelection"/>
/// set true alongside <see cref="PreAuthToken"/>/<see cref="Memberships"/> instead — a single
/// discriminated response type rather than two unrelated shapes on the same endpoint, so the
/// frontend has one place to check <c>requiresBusinessSelection</c> before reading the rest. The
/// original (pre-multi-business) fields are deliberately left in their original parameter order and
/// still populated exactly as before for the single-membership case — only the three new, trailing,
/// defaulted parameters are new, so every existing caller that built this positionally keeps
/// compiling unchanged and the serialized JSON for that case is the same as before plus the three
/// new fields.
/// </summary>
public record AuthResultDto(
    string? AccessToken,
    DateTime? ExpiresAtUtc,
    Guid? UserId,
    Guid? BusinessId,
    string? Email,
    string? Role,
    bool? MustChangePassword,
    bool RequiresBusinessSelection = false,
    string? PreAuthToken = null,
    IReadOnlyList<MembershipOptionDto>? Memberships = null)
{
    /// <summary>The normal, single-membership (or post-selection/switch) success shape.</summary>
    public static AuthResultDto ForToken(
        AuthToken token, Guid userId, Guid businessId, string email, string role, bool mustChangePassword) =>
        new(token.AccessToken, token.ExpiresAtUtc, userId, businessId, email, role, mustChangePassword);

    /// <summary>The "pick a business" shape returned when login matches more than one active membership.</summary>
    public static AuthResultDto ForBusinessSelection(string preAuthToken, IReadOnlyList<MembershipOptionDto> memberships) =>
        new(
            AccessToken: null,
            ExpiresAtUtc: null,
            UserId: null,
            BusinessId: null,
            Email: null,
            Role: null,
            MustChangePassword: null,
            RequiresBusinessSelection: true,
            PreAuthToken: preAuthToken,
            Memberships: memberships);
}

/// <summary>
/// One selectable option in a multi-membership login's business list — only ever returned in the
/// login-selection response body (never baked into a bearer token; see architecture.md's "JWT
/// claims, worked example").
/// </summary>
public record MembershipOptionDto(Guid BusinessId, string BusinessName, string Role);
