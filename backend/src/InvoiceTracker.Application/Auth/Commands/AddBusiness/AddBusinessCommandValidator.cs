using FluentValidation;

namespace InvoiceTracker.Application.Auth.Commands.AddBusiness;

/// <summary>Mirrors RegisterCommandValidator's BusinessName/BusinessEmail rules — same shape, no caller-supplied identity fields here.</summary>
public class AddBusinessCommandValidator : AbstractValidator<AddBusinessCommand>
{
    public AddBusinessCommandValidator()
    {
        RuleFor(x => x.BusinessName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.BusinessEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);
    }
}
