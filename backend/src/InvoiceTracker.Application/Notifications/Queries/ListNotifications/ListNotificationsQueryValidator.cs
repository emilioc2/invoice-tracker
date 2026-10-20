using FluentValidation;

namespace InvoiceTracker.Application.Notifications.Queries.ListNotifications;

public class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
