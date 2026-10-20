using InvoiceTracker.Application.Common.Interfaces;

namespace InvoiceTracker.UnitTests.TestSupport;

/// <summary>
/// Minimal <see cref="ICurrentUserService"/> test double for command/query handler unit tests —
/// handlers only ever read <see cref="BusinessId"/> (resolved server-side from the JWT in
/// production) to scope their EF Core queries, so this just returns a fixed value set up by the
/// test rather than requiring a real JWT/HTTP context.
/// </summary>
public class TestCurrentUserService : ICurrentUserService
{
    public TestCurrentUserService(Guid businessId, Guid? userId = null, string? role = null, string? email = null)
    {
        BusinessId = businessId;
        UserId = userId ?? Guid.NewGuid();
        Role = role ?? "Owner";
        Email = email ?? "owner@example.com";
    }

    public Guid? UserId { get; }
    public Guid? BusinessId { get; }
    public string? Role { get; }
    public string? Email { get; }
}
