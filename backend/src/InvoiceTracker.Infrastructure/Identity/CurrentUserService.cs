using System.Security.Claims;
using InvoiceTracker.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace InvoiceTracker.Infrastructure.Identity;

/// <summary>
/// Reads the authenticated caller's identity off <see cref="HttpContext.User"/>'s claims — the
/// server-side-resolved source of truth for BusinessId everywhere else in the app (handlers must
/// never trust a client-supplied business/tenant id).
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId => ParseGuidClaim(ClaimTypes.NameIdentifier);

    public Guid? BusinessId => ParseGuidClaim("businessId");

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    private Guid? ParseGuidClaim(string claimType)
    {
        var value = Principal?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
