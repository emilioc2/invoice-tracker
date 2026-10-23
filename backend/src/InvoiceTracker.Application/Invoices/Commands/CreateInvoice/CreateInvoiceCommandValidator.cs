using FluentValidation;
using InvoiceTracker.Application.Invoices.Common;

namespace InvoiceTracker.Application.Invoices.Commands.CreateInvoice;

public class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty();

        RuleFor(x => x.DueDate)
            .NotEmpty();

        // A rate (0 to 1), not a percentage-as-100 — e.g. 0.08 for 8%.
        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0m, 1m);

        RuleFor(x => x.Notes)
            .MaximumLength(2000);

        RuleFor(x => x.PaymentDetails)
            .MaximumLength(2000);

        RuleFor(x => x.LineItems)
            .NotEmpty()
            .WithMessage("At least one line item is required.");

        RuleForEach(x => x.LineItems).SetValidator(new InvoiceLineItemRequestValidator());
    }
}

public class InvoiceLineItemRequestValidator : AbstractValidator<InvoiceLineItemRequest>
{
    public InvoiceLineItemRequestValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Quantity)
            .GreaterThan(0m);

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0m);
    }
}
