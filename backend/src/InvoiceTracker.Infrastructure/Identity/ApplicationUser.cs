using Microsoft.AspNetCore.Identity;

namespace InvoiceTracker.Infrastructure.Identity;

/// <summary>
/// Pure credential store — just the stock <see cref="IdentityUser{TKey}"/> fields (UserName,
/// Email, PasswordHash, SecurityStamp, ...). No extra properties: BusinessId/Role/Status live
/// solely on the correlated <see cref="InvoiceTracker.Domain.Entities.User"/> row (same Id), per
/// this project's deliberate separation of "who can log in" (Identity) from "what business/role
/// they belong to" (the Domain model).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
}
