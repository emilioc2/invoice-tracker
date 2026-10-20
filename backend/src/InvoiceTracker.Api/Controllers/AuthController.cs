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
/// The only controller whose routes don't require a JWT — register/login/forgot/reset password.
/// Stays thin: each action only builds a command and calls `_mediator.Send(...)`.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterCommand(request.BusinessName, request.BusinessEmail, request.OwnerEmail, request.Password);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        await _mediator.Send(command, cancellationToken);

        // Deliberately generic — never confirms/denies whether the email matched an account.
        return Ok(new { message = "If an account with that email exists, a password reset email has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return Ok(new { message = "Password has been reset successfully." });
    }
}

public record RegisterRequest(string BusinessName, string BusinessEmail, string OwnerEmail, string Password);

public record LoginRequest(string Email, string Password);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
