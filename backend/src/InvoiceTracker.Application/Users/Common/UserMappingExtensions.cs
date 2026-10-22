using InvoiceTracker.Domain.Entities;

namespace InvoiceTracker.Application.Users.Common;

public static class UserMappingExtensions
{
    public static UserDto ToDto(this User user) =>
        new(user.Id, user.Email, user.Role.ToString(), user.Status.ToString(), user.StatusChangedAt);
}
