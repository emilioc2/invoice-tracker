using InvoiceTracker.Domain.Common;
using InvoiceTracker.Domain.Enums;
using InvoiceTracker.Domain.Exceptions;

namespace InvoiceTracker.Domain.Entities;

public class Client : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAtDate { get; set; }
    public DateTime LastModifiedDate { get; set; }

    // Private setter: every status change must go through TransitionTo below (see
    // architecture.md's "Domain invariants: centralized status transitions"). Starts at Active,
    // matching every client's actual starting state.
    public ClientStatus Status { get; private set; } = ClientStatus.Active;

    // Set by TransitionTo on every transition — records when Status last changed. Kept separate
    // from LastModifiedDate, which bumps on any field edit, not specifically a status change.
    // Defaults to "now" at construction time, since a newly created client's status (Active) was
    // effectively just "set" by creation.
    public DateTime StatusChangedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// The single place a client's status workflow's legal transitions are encoded. The only
    /// legal transition is Active -> Removed (user-facing "Remove") — there is no reverse
    /// transition; a removed client cannot currently be restored. Every caller that changes
    /// <see cref="Status"/> — command handlers — must call this rather than re-implementing the
    /// legality check. Throws <see cref="InvalidClientStatusTransitionException"/> for any other
    /// transition, including a no-op "transition" to the current status.
    /// </summary>
    public void TransitionTo(ClientStatus target)
    {
        var isLegal = (Status, target) switch
        {
            (ClientStatus.Active, ClientStatus.Removed) => true,
            _ => false
        };

        if (!isLegal)
        {
            throw new InvalidClientStatusTransitionException(Status, target);
        }

        Status = target;
        StatusChangedAt = DateTime.UtcNow;
    }
}
