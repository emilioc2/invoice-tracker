using InvoiceTracker.Domain.Common;
using InvoiceTracker.Domain.Enums;

namespace InvoiceTracker.Domain.Entities;

// Note: PasswordHash is intentionally omitted here. ASP.NET Core Identity's
// user store will own password storage; how that store relates to this
// Domain/persistence table is a decision deferred to the Auth build-order
// step (step 3), not this persistence-foundation step.
public class User : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    // Set true when a Staff user is created with an auto-generated temp password (see
    // CreateStaffUserCommandHandler) and cleared back to false on that user's next successful
    // password change via ChangePasswordCommandHandler — see architecture.md's "Forced password
    // change for temp-password logins". Never true for a newly-registered Owner, who chooses
    // their own password at registration.
    public bool MustChangePassword { get; set; }
}
