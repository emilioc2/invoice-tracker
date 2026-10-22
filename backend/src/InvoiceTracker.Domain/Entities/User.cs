using InvoiceTracker.Domain.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

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

    // Private setter: every status change must go through TransitionTo below (see
    // architecture.md's "Domain invariants: centralized status transitions"). Starts at Active,
    // matching every user's actual starting state.
    public UserStatus Status { get; private set; } = UserStatus.Active;

    // Set by TransitionTo on every transition — records when Status last changed. Defaults to
    // "now" at construction time, since a newly created user's status (Active) was effectively
    // just "set" by creation.
    public DateTime StatusChangedAt { get; private set; } = DateTime.UtcNow;

    // Set true when a Staff user is created with an auto-generated temp password (see
    // CreateStaffUserCommandHandler) and cleared back to false on that user's next successful
    // password change via ChangePasswordCommandHandler — see architecture.md's "Forced password
    // change for temp-password logins". Never true for a newly-registered Owner, who chooses
    // their own password at registration.
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// The single place a user's status workflow's legal transitions are encoded. Both
    /// directions are legal: Active -> Deactivated (deactivate) and Deactivated -> Active
    /// (reactivate). Every caller that changes <see cref="Status"/> — command handlers — must
    /// call this rather than re-implementing the legality check. Throws
    /// <see cref="InvalidUserStatusTransitionException"/> for any other transition, including a
    /// no-op "transition" to the current status.
    /// </summary>
    public void TransitionTo(UserStatus target)
    {
        var isLegal = (Status, target) switch
        {
            (UserStatus.Active, UserStatus.Deactivated) => true,
            (UserStatus.Deactivated, UserStatus.Active) => true,
            _ => false
        };

        if (!isLegal)
        {
            throw new InvalidUserStatusTransitionException(Status, target);
        }

        Status = target;
        StatusChangedAt = DateTime.UtcNow;
    }
}
