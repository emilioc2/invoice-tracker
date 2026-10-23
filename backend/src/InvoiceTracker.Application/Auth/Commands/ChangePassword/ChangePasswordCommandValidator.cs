using FluentValidation;
using InvoiceTracker.Application.Common.Validation;

namespace InvoiceTracker.Application.Auth.Commands.ChangePassword;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .PasswordRules();
    }
}
