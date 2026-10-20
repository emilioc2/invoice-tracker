using FluentValidation;

namespace InvoiceTracker.Application.Invoices.Queries.ListInvoices;

public class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
