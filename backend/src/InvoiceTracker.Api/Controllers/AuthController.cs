using InvoiceTracker.Application.Auth.Commands.ChangePassword;
using InvoiceTracker.Application.Auth.Commands.ForgotPassword;
using InvoiceTracker.Application.Auth.Commands.Login;
using InvoiceTracker.Application.Auth.Commands.Register;
using InvoiceTracker.Application.Auth.Commands.ResetPassword;
using InvoiceTracker.Application.Auth.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Auth routes. Register/login/forgot-password/reset-password are pre-auth and individually
/// opt out of the app-wide "every endpoint requires a JWT" fallback policy via
/// <see cref="AllowAnonymousAttribute"/> — deliberately per-action (not class-level) so that
/// change-password, added later, requires a valid JWT like every other controller's routes.
/// Stays thin: each action only builds a command and calls `_mediator.Send(...)`.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterCommand(request.BusinessName, request.BusinessEmail, request.OwnerEmail, request.Password);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        await _mediator.Send(command, cancellationToken);

        // Deliberately generic — never confirms/denies whether the email matched an account.
        return Ok(new { message = "If an account with that email exists, a password reset email has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Password has been reset successfully." });
    }

    /// <summary>
    /// Mid-session password change for an authenticated caller who knows their current
    /// password — distinct from forgot/reset-password above. Requires a valid JWT (no
    /// <see cref="AllowAnonymousAttribute"/>, so the app-wide fallback policy applies); the
    /// explicit <see cref="AuthorizeAttribute"/> below is just documentation of that intent.
    /// Used by Staff users completing the forced change after a temp-password login (see
    /// architecture.md's "Forced password change for temp-password logins").
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ChangePasswordCommand(request.CurrentPassword, request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}

public record RegisterRequest(string BusinessName, string BusinessEmail, string OwnerEmail, string Password);

public record LoginRequest(string Email, string Password);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
