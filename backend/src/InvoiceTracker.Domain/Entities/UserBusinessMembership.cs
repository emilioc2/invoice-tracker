using InvoiceTracker.Domain.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

namespace InvoiceTracker.Domain.Entities;

// Note: PasswordHash is intentionally omitted here — that stays with ASP.NET Core Identity's
// ApplicationUser (the credential store). This row is the (person, business) pairing: one per
// business a given identity belongs to. See requirements.md's "Multi-business membership" section
// for the full model this entity replaces (the former 1:1 Domain.User row).
//
// Id is this row's own primary key — distinct from UserId, which correlates to the identity
// (ApplicationUser.Id) this membership belongs to. A single identity can hold more than one
// UserBusinessMembership row (one per business), each with its own Id but the same UserId.
//
// Deliberately no Email property here: Email lives solely on the identity (ApplicationUser.Email),
// which is this row's single source of truth for that fact. Duplicating it here would be a real
// normalization violation with no feature that needs the copy — anything that needs to display a
// membership's email resolves it via a lookup/join on UserId instead (see IIdentityService).
public class UserBusinessMembership : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BusinessId { get; set; }
    public UserRole Role { get; set; }

    // Private setter: every status change must go through TransitionTo below (see
    // architecture.md's "Domain invariants: centralized status transitions"). Starts at Active,
    // matching every membership's actual starting state.
    public UserStatus Status { get; private set; } = UserStatus.Active;

    // Set by TransitionTo on every transition — records when Status last changed. Defaults to
    // "now" at construction time, since a newly created membership's status (Active) was
    // effectively just "set" by creation.
    public DateTime StatusChangedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// The single place a membership's status workflow's legal transitions are encoded. Both
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
