using InvoiceTracker.Application.Common.Exceptions;
using InvoiceTracker.Application.Common.Interfaces;
using MediatR;

namespace InvoiceTracker.Application.Auth.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IIdentityService _identityService;

    public ResetPasswordCommandHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var result = await _identityService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            throw new IdentityOperationException(result.Errors);
        }

        return Unit.Value;
    }
}
