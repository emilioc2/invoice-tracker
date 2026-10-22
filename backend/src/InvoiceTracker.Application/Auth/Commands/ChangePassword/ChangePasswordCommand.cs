using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Mid-session password change for an authenticated caller who knows their current password —
/// distinct from the reset-token-based ForgotPassword/ResetPassword pair. Used both for the
/// forced change a Staff user must complete after logging in with a temp password (see
/// architecture.md's "Forced password change for temp-password logins") and for any user
/// changing their password voluntarily. The caller's identity comes from
/// <c>ICurrentUserService</c> inside the handler — never a client-supplied id.
/// </summary>
public record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;
