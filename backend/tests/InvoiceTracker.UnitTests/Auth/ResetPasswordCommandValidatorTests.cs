using FluentValidation.TestHelper;
using InvoiceTracker.Application.Auth.Commands.ResetPassword;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="ResetPasswordCommandValidator"/>.
/// </summary>
public class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    private static ResetPasswordCommand MakeValidCommand() =>
        new("owner@example.com", "some-reset-token", "Password1");

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Email = email });
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void EmptyToken_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Token = "" });
        result.ShouldHaveValidationErrorFor(x => x.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1A")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public void InvalidNewPassword_IsRejected(string password)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { NewPassword = password });
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void NewPasswordMeetingAllComplexityRules_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { NewPassword = "Password1" });
        result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
    }
}
