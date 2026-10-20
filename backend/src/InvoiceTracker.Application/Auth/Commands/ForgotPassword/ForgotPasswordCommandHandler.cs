using InvoiceTracker.Application.Common.Interfaces;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Generates a reset token and emails it (no-op <see cref="IEmailSender"/> for now — the real
/// Formspree implementation is a later build-order step). Always succeeds from the caller's
/// point of view, whether or not the email matched an account, so the response never leaks
/// account existence — standard practice for forgot-password flows.
/// </summary>
public class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Unit>
{
    private readonly IIdentityService _identityService;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordCommandHandler(IIdentityService identityService, IEmailSender emailSender)
    {
        _identityService = identityService;
        _emailSender = emailSender;
    }

    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var token = await _identityService.GeneratePasswordResetTokenAsync(request.Email);

        if (token is not null)
        {
            var body = $"Use the following token to reset your Invoice Tracker password: {token}";
            await _emailSender.SendAsync(request.Email, "Reset your Invoice Tracker password", body, cancellationToken);
        }

        return Unit.Value;
    }
}
