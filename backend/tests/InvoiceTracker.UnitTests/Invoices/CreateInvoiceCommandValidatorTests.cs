using FluentValidation.TestHelper;
using InvoiceTracker.Application.Invoices.Commands.CreateInvoice;
using InvoiceTracker.Application.Invoices.Common;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// <see cref="FluentValidation.TestHelper"/>-based tests for every rule actually declared by
/// <see cref="CreateInvoiceCommandValidator"/>: empty line items, negative/zero quantity, negative
/// price, missing ClientId/DueDate, out-of-range TaxRate, and an overlong Notes/Description — per
/// architecture.md's example ("rejects empty line items, negative quantity/price, missing
/// ClientId"), plus the valid-input-passes case.
/// </summary>
public class CreateInvoiceCommandValidatorTests
{
    private readonly CreateInvoiceCommandValidator _validator = new();

    private static CreateInvoiceCommand MakeValidCommand() => new(
        Guid.NewGuid(),
        DateTime.UtcNow.AddDays(30),
        0.08m,
        "Thanks for your business",
        new List<InvoiceLineItemRequest> { new("Consulting", 2m, 150m) });

    [Fact]
    public void ValidCommand_PassesWithNoErrors()
    {
        var result = _validator.TestValidate(MakeValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyClientId_IsRejected()
    {
        var command = MakeValidCommand() with { ClientId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.ClientId);
    }

    [Fact]
    public void DefaultDueDate_IsRejected()
    {
        var command = MakeValidCommand() with { DueDate = default };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.DueDate);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void TaxRateOutsideZeroToOneInclusive_IsRejected(decimal taxRate)
    {
        var command = MakeValidCommand() with { TaxRate = taxRate };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.TaxRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TaxRateAtInclusiveBoundary_IsAccepted(decimal taxRate)
    {
        var command = MakeValidCommand() with { TaxRate = taxRate };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.TaxRate);
    }

    [Fact]
    public void NotesLongerThan2000Characters_IsRejected()
    {
        var command = MakeValidCommand() with { Notes = new string('a', 2001) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void NullNotes_IsAccepted()
    {
        var command = MakeValidCommand() with { Notes = null };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void EmptyLineItems_IsRejected()
    {
        var command = MakeValidCommand() with { LineItems = new List<InvoiceLineItemRequest>() };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.LineItems);
    }

    [Fact]
    public void LineItemWithEmptyDescription_IsRejected()
    {
        var command = MakeValidCommand() with
        {
            LineItems = new List<InvoiceLineItemRequest> { new("", 1m, 10m) }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("LineItems[0].Description");
    }

    [Fact]
    public void LineItemWithDescriptionLongerThan500Characters_IsRejected()
    {
        var command = MakeValidCommand() with
        {
            LineItems = new List<InvoiceLineItemRequest> { new(new string('x', 501), 1m, 10m) }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("LineItems[0].Description");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LineItemWithZeroOrNegativeQuantity_IsRejected(decimal quantity)
    {
        var command = MakeValidCommand() with
        {
            LineItems = new List<InvoiceLineItemRequest> { new("Widget", quantity, 10m) }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("LineItems[0].Quantity");
    }

    [Fact]
    public void LineItemWithNegativeUnitPrice_IsRejected()
    {
        var command = MakeValidCommand() with
        {
            LineItems = new List<InvoiceLineItemRequest> { new("Widget", 1m, -0.01m) }
        };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor("LineItems[0].UnitPrice");
    }

    [Fact]
    public void LineItemWithZeroUnitPrice_IsAccepted()
    {
        // GreaterThanOrEqualTo(0m) on UnitPrice -- a free line item is a valid use case
        // (e.g. a goodwill credit line), unlike Quantity which must be strictly positive.
        var command = MakeValidCommand() with
        {
            LineItems = new List<InvoiceLineItemRequest> { new("Goodwill credit", 1m, 0m) }
        };
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor("LineItems[0].UnitPrice");
    }
}
