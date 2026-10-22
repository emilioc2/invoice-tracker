using FluentValidation;
using InvoiceTracker.Application.Common.Validation;

namespace InvoiceTracker.Application.Notifications.Queries.ListNotifications;

public class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsQueryValidator()
    {
        this.PaginationRules(x => x.PageNumber, x => x.PageSize);
    }
}
