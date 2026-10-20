using FluentValidation.TestHelper;
using InvoiceTracker.Application.Users.Commands.CreateStaffUser;

namespace InvoiceTracker.UnitTests.Users;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="CreateStaffUserCommandValidator"/>. Note: BusinessId is deliberately not part of
/// this command's shape (resolved server-side from the JWT in the handler) so there's no rule for
/// it to test here.
/// </summary>
public class CreateStaffUserCommandValidatorTests
{
    private readonly CreateStaffUserCommandValidator _validator = new();

    [Fact]
    public void ValidEmail_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(new CreateStaffUserCommand("staff@example.com"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(new CreateStaffUserCommand(email));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void EmailLongerThan256Characters_IsRejected()
    {
        var longEmail = new string('a', 250) + "@example.com";
        var result = _validator.TestValidate(new CreateStaffUserCommand(longEmail));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
