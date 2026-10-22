using FluentValidation;

namespace InvoiceTracker.Application.Auth.Commands.SwitchBusiness;

public class SwitchBusinessCommandValidator : AbstractValidator<SwitchBusinessCommand>
{
    public SwitchBusinessCommandValidator()
    {
        RuleFor(x => x.BusinessId)
            .NotEmpty();
    }
}
