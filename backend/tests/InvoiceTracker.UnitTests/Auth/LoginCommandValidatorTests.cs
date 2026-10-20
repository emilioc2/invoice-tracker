using FluentValidation.TestHelper;
using InvoiceTracker.Application.Auth.Commands.Login;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="LoginCommandValidator"/>.
/// </summary>
public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(new LoginCommand("owner@example.com", "whatever-the-password-is"));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(new LoginCommand(email, "whatever-the-password-is"));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void EmptyPassword_IsRejected()
    {
        // Deliberately no complexity/length rules on login's password -- unlike
        // register/reset-password, this field is just "was something supplied", since the actual
        // check against stored credentials happens in the handler via Identity.
        var result = _validator.TestValidate(new LoginCommand("owner@example.com", ""));
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}
