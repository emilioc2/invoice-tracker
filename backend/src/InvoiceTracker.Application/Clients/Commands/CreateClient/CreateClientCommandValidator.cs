using FluentValidation;

namespace InvoiceTracker.Application.Clients.Commands.CreateClient;

public class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Phone)
            .MaximumLength(50);

        RuleFor(x => x.Address)
            .MaximumLength(500);
    }
}
