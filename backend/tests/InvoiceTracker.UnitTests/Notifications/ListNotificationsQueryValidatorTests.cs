using FluentValidation.TestHelper;
using InvoiceTracker.Application.Notifications.Queries.ListNotifications;

namespace InvoiceTracker.UnitTests.Notifications;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for the pagination bounds
/// <see cref="ListNotificationsQueryValidator"/> applies via the shared
/// <c>RuleBuilderExtensions.PaginationRules()</c> helper (architecture.md's "Shared rules across
/// validators" note): <c>PageNumber</c> must be at least 1, and <c>PageSize</c> must be between 1
/// and 100 (inclusive).
/// </summary>
public class ListNotificationsQueryValidatorTests
{
    private readonly ListNotificationsQueryValidator _validator = new();

    private static ListNotificationsQuery MakeValidQuery() => new(
        InvoiceId: null,
        PageNumber: 1,
        PageSize: 20);

    [Fact]
    public void ValidQuery_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidQuery());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PageNumberZero_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageNumber = 0 });
        result.ShouldHaveValidationErrorFor(x => x.PageNumber);
    }

    [Fact]
    public void PageNumberOne_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageNumber = 1 });
        result.ShouldNotHaveValidationErrorFor(x => x.PageNumber);
    }

    [Fact]
    public void PageSizeZero_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageSize = 0 });
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSizeOne_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageSize = 1 });
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSizeOneHundred_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageSize = 100 });
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void PageSizeOneHundredAndOne_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidQuery() with { PageSize = 101 });
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }
}
