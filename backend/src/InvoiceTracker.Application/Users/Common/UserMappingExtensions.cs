using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Users.Common;

public static class UserMappingExtensions
{
    /// <summary>
    /// <paramref name="email"/> is passed in explicitly rather than read off <paramref name="membership"/>
    /// — <see cref="UserBusinessMembership"/> deliberately has no Email property of its own (email
    /// lives solely on the correlated identity); callers resolve it via
    /// <see cref="InvoiceTracker.Application.Common.Interfaces.IIdentityService"/> (or already have
    /// it on hand, e.g. from the command that just created the identity) before mapping.
    /// </summary>
    public static UserDto ToDto(this UserBusinessMembership membership, string email) =>
        new(membership.Id, email, membership.Role.ToString(), membership.Status.ToString(), membership.StatusChangedAt);
}
