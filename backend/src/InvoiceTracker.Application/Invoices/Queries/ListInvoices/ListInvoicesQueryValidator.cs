using FluentValidation;
using InvoiceTracker.Application.Common.Validation;

namespace InvoiceTracker.Application.Invoices.Queries.ListInvoices;

public class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator()
    {
        this.PaginationRules(x => x.PageNumber, x => x.PageSize);
    }
}
