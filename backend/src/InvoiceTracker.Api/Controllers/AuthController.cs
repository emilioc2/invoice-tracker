using InvoiceTracker.Application.Auth.Commands.AddBusiness;
using InvoiceTracker.Application.Auth.Commands.ChangePassword;
using InvoiceTracker.Application.Auth.Commands.ForgotPassword;
using InvoiceTracker.Application.Auth.Commands.Login;
using InvoiceTracker.Application.Auth.Commands.Register;
using InvoiceTracker.Application.Auth.Commands.ResetPassword;
using InvoiceTracker.Application.Auth.Commands.SelectBusiness;
using InvoiceTracker.Application.Auth.Commands.SwitchBusiness;
using InvoiceTracker.Application.Auth.Common;
using InvoiceTracker.Application.Auth.Queries.GetMyMemberships;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceTracker.Api.Controllers;

/// <summary>
/// Auth routes. Register/login/forgot-password/reset-password are pre-auth and individually
/// opt out of the app-wide "every endpoint requires a JWT" fallback policy via
/// <see cref="AllowAnonymousAttribute"/> — deliberately per-action (not class-level) so that
/// change-password/switch-business/businesses require a valid JWT like every other controller's
/// routes. select-business is also <see cref="AllowAnonymousAttribute"/> (the caller has no normal
/// JWT yet at this point — only the preauth token <c>LoginCommandHandler</c> handed back), but it is
/// not a "skip auth entirely" route the way the other four are: the preauth token is a request-body
/// field here (<c>{ preAuthToken, businessId }</c>), not an Authorization header, so
/// <c>SelectBusinessCommandHandler</c> performs its own real signature/expiry/claim validation on it
/// (see <see cref="InvoiceTracker.Application.Common.Interfaces.IJwtTokenGenerator.ValidatePreAuthToken"/>)
/// rather than relying on (or needing) the standard JWT bearer pipeline for this one call. Separately,
/// that same pipeline's <c>OnTokenValidated</c> event (see <c>DependencyInjection.AddJwtBearer</c>)
/// rejects a preauth-claim-bearing token from authenticating as a normal Bearer token against *any*
/// endpoint, including this one — its only valid use is this field.
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

    /// <summary>
    /// Completes a login that returned <c>requiresBusinessSelection: true</c>. See this class's own
    /// doc comment for why this is <see cref="AllowAnonymousAttribute"/> yet still performs real
    /// token validation (inside <c>SelectBusinessCommandHandler</c>), rather than skipping auth.
    /// </summary>
    [HttpPost("select-business")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResultDto>> SelectBusiness(SelectBusinessRequest request, CancellationToken cancellationToken)
    {
        var command = new SelectBusinessCommand(request.PreAuthToken, request.BusinessId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Requires a valid (normal, non-preauth) JWT. Switches the caller's active session to a
    /// different one of their own active memberships, issuing a brand-new JWT — the previous token
    /// is not revoked. See requirements.md's "Multi-business membership".
    /// </summary>
    [HttpPost("switch-business")]
    [Authorize]
    public async Task<ActionResult<AuthResultDto>> SwitchBusiness(SwitchBusinessRequest request, CancellationToken cancellationToken)
    {
        var command = new SwitchBusinessCommand(request.BusinessId);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Requires a valid JWT ("Add a business" — requirements.md's "Multi-business membership").
    /// Creates a new Business + a new Owner membership for the caller's existing identity (no new
    /// identity/password), then behaves like switch-business into it.
    /// </summary>
    [HttpPost("businesses")]
    [Authorize]
    public async Task<ActionResult<AuthResultDto>> AddBusiness(AddBusinessRequest request, CancellationToken cancellationToken)
    {
        var command = new AddBusinessCommand(request.BusinessName, request.BusinessEmail);
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Requires a valid (normal, non-preauth) JWT. Returns the caller's own active memberships —
    /// lets the frontend's "Switch business" affordance do a real lazy fetch on open, rather than
    /// relying only on whatever <c>memberships</c> list a prior multi-membership login/select-business
    /// response happened to return earlier in this session.
    /// </summary>
    [HttpGet("memberships")]
    [Authorize]
    public async Task<ActionResult<List<MembershipOptionDto>>> GetMyMemberships(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyMembershipsQuery(), cancellationToken);
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
    /// Mid-session, forced password change — distinct from forgot/reset-password above. Requires
    /// a valid JWT (no <see cref="AllowAnonymousAttribute"/>, so the app-wide fallback policy
    /// applies); the explicit <see cref="AuthorizeAttribute"/> below is just documentation of that
    /// intent. Takes only the new password: the caller already holds a valid JWT issued moments
    /// earlier at the login that used the temp password, which is proof enough, so there is no
    /// current-password field or verification step. Used by Staff users completing the forced
    /// change after a temp-password login (see architecture.md's "Forced password change for
    /// temp-password logins") — this is the product's only password-change-while-logged-in route.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ChangePasswordCommand(request.NewPassword);
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}

public record RegisterRequest(string BusinessName, string BusinessEmail, string OwnerEmail, string Password);

public record LoginRequest(string Email, string Password);

public record SelectBusinessRequest(string PreAuthToken, Guid BusinessId);

public record SwitchBusinessRequest(Guid BusinessId);

public record AddBusinessRequest(string BusinessName, string BusinessEmail);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record ChangePasswordRequest(string NewPassword);
