namespace InvoiceTracker.Api.Startup;

/// <summary>
/// Named rate-limiter policy identifiers, shared between the policy registration in
/// <c>Program.cs</c> (<c>AddRateLimiter</c>) and the controller actions that opt into one via
/// <c>[EnableRateLimiting(...)]</c> — a single constant avoids the two sides drifting apart.
/// </summary>
public static class RateLimiterPolicyNames
{
    /// <summary>
    /// Applied to the brute-force-sensitive auth actions (login, register, forgot/reset/change
    /// password) — see <c>AuthController</c>.
    /// </summary>
    public const string Auth = "auth";
}
