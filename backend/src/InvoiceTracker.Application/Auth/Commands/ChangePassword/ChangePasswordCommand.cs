using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Mid-session, forced password change for a Staff user who just logged in with a temp password
/// (see architecture.md's "Forced password change for temp-password logins") — distinct from the
/// reset-token-based ForgotPassword/ResetPassword pair. Takes only the new password: the caller
/// already holds a valid JWT issued moments earlier at the exact login that used the temp
/// password, which is proof enough, so there is no <c>currentPassword</c> field and no
/// verification step against it. The caller's identity comes from <c>ICurrentUserService</c>
/// inside the handler — never a client-supplied id. This is the product's only
/// password-change-while-logged-in flow; there is no separate voluntary/self-service variant.
/// </summary>
public record ChangePasswordCommand(string NewPassword) : IRequest<Unit>;
