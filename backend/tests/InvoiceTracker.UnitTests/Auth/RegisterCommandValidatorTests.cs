using FluentValidation.TestHelper;
using InvoiceTracker.Application.Auth.Commands.Register;

namespace InvoiceTracker.UnitTests.Auth;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="RegisterCommandValidator"/>.
/// </summary>
public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand MakeValidCommand() =>
        new("Acme Co", "acme@example.com", "owner@example.com", "Password1");

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyBusinessName_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { BusinessName = "" });
        result.ShouldHaveValidationErrorFor(x => x.BusinessName);
    }

    [Fact]
    public void BusinessNameLongerThan200Characters_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { BusinessName = new string('a', 201) });
        result.ShouldHaveValidationErrorFor(x => x.BusinessName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidBusinessEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { BusinessEmail = email });
        result.ShouldHaveValidationErrorFor(x => x.BusinessEmail);
    }

    [Fact]
    public void BusinessEmailLongerThan256Characters_IsRejected()
    {
        var longEmail = new string('a', 250) + "@example.com";
        var result = _validator.TestValidate(MakeValidCommand() with { BusinessEmail = longEmail });
        result.ShouldHaveValidationErrorFor(x => x.BusinessEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void InvalidOwnerEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { OwnerEmail = email });
        result.ShouldHaveValidationErrorFor(x => x.OwnerEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1A")] // 7 chars -- below the 8-char minimum
    [InlineData("alllowercase1")] // no uppercase
    [InlineData("ALLUPPERCASE1")] // no lowercase
    [InlineData("NoDigitsHere")] // no digit
    public void InvalidPassword_IsRejected(string password)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Password = password });
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void PasswordMeetingAllComplexityRules_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Password = "Password1" });
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }
}
