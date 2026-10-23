using FluentValidation.TestHelper;
using InvoiceTracker.Application.Auth.Commands.ChangePassword;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="ChangePasswordCommandValidator"/> — mirrors <c>ResetPasswordCommandValidatorTests</c>,
/// confirming <c>NewPassword</c> goes through the same shared <c>RuleBuilderExtensions.PasswordRules()</c>.
/// There is no <c>CurrentPassword</c> field/rule — see <see cref="ChangePasswordCommand"/>'s own
/// doc comment.
/// </summary>
public class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    private static ChangePasswordCommand MakeValidCommand() =>
        new("NewPassword1");

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1A")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public void InvalidNewPassword_IsRejected(string newPassword)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { NewPassword = newPassword });
        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Fact]
    public void NewPasswordMeetingAllComplexityRules_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { NewPassword = "NewPassword1" });
        result.ShouldNotHaveValidationErrorFor(x => x.NewPassword);
    }
}
