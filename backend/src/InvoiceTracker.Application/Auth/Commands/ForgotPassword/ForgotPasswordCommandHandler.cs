using InvoiceTracker.Application.Common.Interfaces;
using InvoiceTracker.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Options;

namespace InvoiceTracker.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Generates a reset token and emails it via <see cref="IEmailSender"/> (the concrete adapter —
/// <c>ResendEmailSender</c> or <c>NoOpEmailSender</c> — is chosen in Infrastructure's DI wiring).
/// Always succeeds from the caller's
/// point of view, whether or not the email matched an account, so the response never leaks
/// account existence — standard practice for forgot-password flows.
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

    public ForgotPasswordCommandHandler(
        IIdentityService identityService,
        IEmailSender emailSender,
        IOptions<FrontendSettings> frontendSettings)
    {
        _identityService = identityService;
        _emailSender = emailSender;
        _frontendSettings = frontendSettings.Value;
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

            await _emailSender.SendAsync(
                request.Email,
                "Reset your Eternity Invoicing password",
                body,
                Array.Empty<EmailAttachment>(),
                cancellationToken);
        }

        return Unit.Value;
    }
}
