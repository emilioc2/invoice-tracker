using Microsoft.AspNetCore.Identity;

namespace InvoiceTracker.Infrastructure.Identity;

/// <summary>
/// Credential store — the stock <see cref="IdentityUser{TKey}"/> fields (UserName, Email,
/// PasswordHash, SecurityStamp, ...) plus exactly one extra property, <see cref="MustChangePassword"/>.
/// BusinessId/Role/Status still live solely on the correlated
/// <see cref="InvoiceTracker.Domain.Entities.UserBusinessMembership"/> row(s) (one per business this
/// identity belongs to, correlated via <c>UserBusinessMembership.UserId</c>), per this project's
/// deliberate separation of "who can log in" (Identity) from "what business/role they belong to"
/// (the Domain model).
///
/// <see cref="MustChangePassword"/> is the one property that crosses that line deliberately: a
/// temp password is a property of this identity's one credential (email + password), not of any
/// particular business membership — the same person logging into two different businesses still
/// has exactly one password to change, not one per membership. See requirements.md's "Multi-business
/// membership" section for the full reasoning.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; }
}
