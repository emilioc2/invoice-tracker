using FluentAssertions;
using InvoiceTracker.Application.Invoices.Common;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Unit tests for <see cref="InvoiceTotalsCalculator"/> per architecture.md's test plan:
/// LineTotal = Quantity x UnitPrice, Subtotal = sum of line totals, TaxAmount = Subtotal x
/// TaxRate, Total = Subtotal + TaxAmount, including a rounding/precision case.
/// </summary>
public class InvoiceTotalsCalculatorTests
{
    [Fact]
    public void BuildLineItems_ComputesLineTotal_AsQuantityTimesUnitPrice()
    {
        var requests = new List<InvoiceLineItemRequest>
        {
            new("Consulting hours", 3m, 150m),
            new("Widgets", 10m, 2.5m)
        };

        var lineItems = InvoiceTotalsCalculator.BuildLineItems(requests);

        lineItems.Should().HaveCount(2);
        lineItems[0].LineTotal.Should().Be(450m);
        lineItems[1].LineTotal.Should().Be(25m);
    }

    [Fact]
    public void Calculate_SumsLineTotalsIntoSubtotal_AndAppliesTaxRateToGetTaxAmountAndTotal()
    {
        var requests = new List<InvoiceLineItemRequest>
        {
            new("Consulting hours", 3m, 150m), // 450.00
            new("Widgets", 10m, 2.5m)          // 25.00
        };
        var lineItems = InvoiceTotalsCalculator.BuildLineItems(requests);

        var (subtotal, taxAmount, total) = InvoiceTotalsCalculator.Calculate(lineItems, 0.08m);

        subtotal.Should().Be(475m);
        taxAmount.Should().Be(38m);
        total.Should().Be(513m);
    }

    [Fact]
    public void Calculate_WithZeroTaxRate_LeavesTaxAmountZero_AndTotalEqualsSubtotal()
    {
        var lineItems = InvoiceTotalsCalculator.BuildLineItems(new List<InvoiceLineItemRequest>
        {
            new("Flat fee", 1m, 1000m)
        });

        var (subtotal, taxAmount, total) = InvoiceTotalsCalculator.Calculate(lineItems, 0m);

        subtotal.Should().Be(1000m);
        taxAmount.Should().Be(0m);
        total.Should().Be(1000m);
    }

    [Fact]
    public void Calculate_WithValuesThatDoNotDivideEvenlyToTwoDecimalPlaces_PreservesExactDecimalPrecision()
    {
        // 3 x 10.005 = 30.015 -- a line total that doesn't land on a whole cent, exercised to
        // prove the calculator uses C#'s decimal type (exact base-10 arithmetic) rather than
        // floating point, which would otherwise introduce representation error here.
        var lineItems = InvoiceTotalsCalculator.BuildLineItems(new List<InvoiceLineItemRequest>
        {
            new("Odd unit price", 3m, 10.005m)
        });

        lineItems[0].LineTotal.Should().Be(30.015m);

        var (subtotal, taxAmount, total) = InvoiceTotalsCalculator.Calculate(lineItems, 0.1m);

        subtotal.Should().Be(30.015m);
        taxAmount.Should().Be(3.0015m);
        total.Should().Be(33.0165m);
    }
}
