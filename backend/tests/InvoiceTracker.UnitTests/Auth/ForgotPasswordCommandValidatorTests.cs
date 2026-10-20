using FluentValidation.TestHelper;
using InvoiceTracker.Application.Auth.Commands.ForgotPassword;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="ForgotPasswordCommandValidator"/>.
/// </summary>
public class ForgotPasswordCommandValidatorTests
{
    private readonly ForgotPasswordCommandValidator _validator = new();

    [Fact]
    public void ValidEmail_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(new ForgotPasswordCommand("owner@example.com"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(new ForgotPasswordCommand(email));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }
}
