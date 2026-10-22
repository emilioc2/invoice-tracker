using FluentValidation;
using InvoiceTracker.Application.Common.Validation;

namespace InvoiceTracker.Application.Clients.Queries.ListClients;

public class ListClientsQueryValidator : AbstractValidator<ListClientsQuery>
{
    public ListClientsQueryValidator()
    {
        this.PaginationRules(x => x.PageNumber, x => x.PageSize);
    }
}
