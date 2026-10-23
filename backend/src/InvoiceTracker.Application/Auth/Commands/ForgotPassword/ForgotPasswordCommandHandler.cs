using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Generates a reset token and emails it via <see cref="IEmailSender"/> (the concrete adapter —
/// <c>ResendEmailSender</c> or <c>NoOpEmailSender</c> — is chosen in Infrastructure's DI wiring).
/// Always succeeds from the caller's
/// point of view, whether or not the email matched an account, so the response never leaks
/// account existence — standard practice for forgot-password flows. This includes a send failure
/// (e.g. a live provider rejecting the recipient): logged and swallowed rather than rethrown, same
/// as the equivalent fix in <c>CreateStaffUserCommandHandler</c> — there's no DB write to roll back
/// here, so surfacing the exception would only turn an already-handled case into a bare 500 that
/// also leaks account existence (the opposite of this handler's whole point).
///
/// The emailed body is a clickable link to the frontend's reset-password page with
/// <c>email</c>/<c>token</c> pre-filled as query params (<c>ResetPasswordPage.tsx</c> already reads
/// and pre-fills from these) rather than a raw token the user would have to copy-paste — both
/// values are URL-encoded since the token in particular contains characters (<c>/</c>, <c>+</c>,
/// <c>=</c>) that are not safe unescaped in a query string.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Unit>
{
    private readonly IIdentityService _identityService;
    private readonly IEmailSender _emailSender;
    private readonly FrontendSettings _frontendSettings;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IIdentityService identityService,
        IEmailSender emailSender,
        IOptions<FrontendSettings> frontendSettings,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _identityService = identityService;
        _emailSender = emailSender;
        _frontendSettings = frontendSettings.Value;
        _logger = logger;
    }

    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var token = await _identityService.GeneratePasswordResetTokenAsync(request.Email);

        if (token is not null)
        {
            var resetLink =
                $"{_frontendSettings.BaseUrl}/reset-password?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";

            var body =
                "Click the link below to reset your Eternity Invoicing password:\n\n" +
                $"{resetLink}\n\n" +
                "If you didn't request this, you can safely ignore this email.";

            // A send failure here (e.g. the live email provider rejecting the recipient) must not
            // propagate — there's no DB write to roll back, and the whole point of this handler is
            // to never let the response reveal whether the email matched an account.
            try
            {
                await _emailSender.SendAsync(
                    request.Email,
                    "Reset your Eternity Invoicing password",
                    body,
                    Array.Empty<EmailAttachment>(),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}.", request.Email);
            }
        }

        return Unit.Value;
    }
}
