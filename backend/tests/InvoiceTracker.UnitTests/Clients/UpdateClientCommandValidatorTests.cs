using FluentValidation.TestHelper;
using InvoiceTracker.Application.Clients.Commands.UpdateClient;

namespace InvoiceTracker.UnitTests.Clients;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="UpdateClientCommandValidator"/>, including the shared <c>PhoneRules</c> rule set
/// (<see cref="InvoiceTracker.Application.Common.Validation.RuleBuilderExtensions.PhoneRules{T}"/>):
/// max length 15, digits only, and that <c>null</c>/empty Phone remains optional.
/// </summary>
public class UpdateClientCommandValidatorTests
{
    private readonly UpdateClientCommandValidator _validator = new();

    private static UpdateClientCommand MakeValidCommand() =>
        new(Guid.NewGuid(), "Acme Co", "client@example.com", "0123456789", "1 Main Street");

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyId_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Id = Guid.Empty });
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void EmptyName_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
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
    public void NullPhone_IsAccepted()
    {
        // Phone is optional on Client -- a null value must not trip the shared PhoneRules.
        var result = _validator.TestValidate(MakeValidCommand() with { Phone = null });
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void EmptyPhone_IsAccepted()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Phone = "" });
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0123456789")]
    [InlineData("123456789012345")] // exactly 15 digits
    public void DigitsOnlyPhoneAtOrBelow15Characters_IsAccepted(string phone)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Phone = phone });
        result.ShouldNotHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void PhoneLongerThan15Digits_IsRejected()
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Phone = new string('1', 16) });
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Theory]
    [InlineData("+123456789")] // leading plus
    [InlineData("012 345 6789")] // spaces
    [InlineData("012-345-6789")] // dashes
    [InlineData("(012)3456789")] // parentheses
    [InlineData("012345678a")] // letters
    public void PhoneWithNonDigitCharacters_IsRejected(string phone)
    {
        var result = _validator.TestValidate(MakeValidCommand() with { Phone = phone });
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }
}
