using FluentValidation;
using InvoiceTracker.Application.Invoices.Commands.CreateInvoice;

namespace InvoiceTracker.Application.Invoices.Commands.UpdateInvoice;

public class UpdateInvoiceCommandValidator : AbstractValidator<UpdateInvoiceCommand>
{
    public UpdateInvoiceCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();

        RuleFor(x => x.ClientId)
            .NotEmpty();

        RuleFor(x => x.DueDate)
            .NotEmpty();

        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0m, 1m);

        RuleFor(x => x.Notes)
            .MaximumLength(2000);

        RuleFor(x => x.LineItems)
            .NotEmpty()
            .WithMessage("At least one line item is required.");

        RuleForEach(x => x.LineItems).SetValidator(new InvoiceLineItemRequestValidator());
    }
}
