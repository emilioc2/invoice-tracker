using System.Reflection;
using FluentAssertions;
using InvoiceTracker.Application.Invoices.Queries.ListInvoices;

namespace InvoiceTracker.UnitTests.Invoices;

/// <summary>
/// Regression coverage for <see cref="ListInvoicesQueryHandler"/>'s private
/// <c>NormalizeToUtc</c> helper, which binds query-string dates (e.g. <c>dateFrom</c>/<c>dateTo</c>)
/// before they're used in an EF Core predicate. Mirrors
/// <see cref="InvoiceTracker.UnitTests.Serialization.UtcDateTimeJsonConverterTests"/> — see
/// architecture.md's "API implementation notes" -> Date/time handling. The case that matters most
/// is Kind = Local (an explicit non-"Z" offset in the query string): the value must be converted
/// via <c>ToUniversalTime()</c> before being relabeled Utc, not merely relabeled in place.
/// </summary>
public class ListInvoicesQueryHandlerNormalizeToUtcTests
{
    private static DateTime NormalizeToUtc(DateTime value)
    {
        var method = typeof(ListInvoicesQueryHandler).GetMethod(
            "NormalizeToUtc", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ListInvoicesQueryHandler), "NormalizeToUtc");

        return (DateTime)method.Invoke(null, new object[] { value })!;
    }

    [Fact]
    public void Local_ExplicitNonZeroOffset_ConvertsToCorrectUtcInstant_NotMerelyRelabeled()
    {
        // ASP.NET Core's model binder parses "2026-01-01T05:00:00-05:00" (an explicit non-"Z"
        // offset) to Kind = Local. The correct UTC instant is 10:00Z. The old relabel-only bug
        // would have produced 05:00Z instead (same wall-clock digits, wrong instant).
        var parsed = DateTime.Parse("2026-01-01T05:00:00-05:00");
        parsed.Kind.Should().Be(DateTimeKind.Local); // sanity-check the premise of this test

        var result = NormalizeToUtc(parsed);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Local_ExplicitPositiveOffset_ConvertsToCorrectUtcInstant()
    {
        var parsed = DateTime.Parse("2026-01-01T09:00:00+09:00");
        parsed.Kind.Should().Be(DateTimeKind.Local);

        var result = NormalizeToUtc(parsed);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Utc_PassesThroughUnchanged()
    {
        var value = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var result = NormalizeToUtc(value);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(value);
    }

    [Fact]
    public void Unspecified_BareDate_IsRelabeledAsUtcWithoutShifting()
    {
        // A bare date-only query value (e.g. ?dateFrom=2026-01-01) binds to Kind = Unspecified —
        // no offset to correct for, so it's simply relabeled, preserving the wall-clock digits.
        var value = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var result = NormalizeToUtc(value);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
