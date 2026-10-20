using FluentValidation;

namespace InvoiceTracker.Application.Clients.Queries.ListClients;

public class ListClientsQueryValidator : AbstractValidator<ListClientsQuery>
{
    public ListClientsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
