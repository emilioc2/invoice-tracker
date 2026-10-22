using FluentValidation;

namespace InvoiceTracker.Application.Auth.Commands.SelectBusiness;

public class SelectBusinessCommandValidator : AbstractValidator<SelectBusinessCommand>
{
    public SelectBusinessCommandValidator()
    {
        RuleFor(x => x.PreAuthToken)
            .NotEmpty();

        RuleFor(x => x.BusinessId)
            .NotEmpty();
    }
}
