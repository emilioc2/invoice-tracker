using System.Text.Json;
using FluentAssertions;
using InvoiceTracker.Api.Serialization;

namespace InvoiceTracker.UnitTests.Serialization;

/// <summary>
/// Regression coverage for <see cref="UtcDateTimeJsonConverter.Read"/>'s three-way
/// <see cref="DateTimeKind"/> branch (see architecture.md's "API implementation notes" ->
/// Date/time handling). The case that matters most is an explicit non-"Z" UTC offset: the
/// value must be converted to the correct UTC instant via <c>ToUniversalTime()</c>, not merely
/// relabeled via <c>SpecifyKind</c> — relabeling alone would silently shift the represented
/// instant by the offset.
/// </summary>
public class UtcDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UtcDateTimeJsonConverter() }
    };

    [Fact]
    public void Read_ExplicitNonZeroOffset_ConvertsToCorrectUtcInstant_NotMerelyRelabeled()
    {
        // -05:00 offset: the correct UTC instant is 10:00Z. The old relabel-only bug would
        // have produced 05:00Z instead (same wall-clock digits, wrong instant).
        var json = "\"2026-01-01T05:00:00-05:00\"";

        var result = JsonSerializer.Deserialize<DateTime>(json, Options);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Read_ExplicitPositiveOffset_ConvertsToCorrectUtcInstant()
    {
        // +09:00 offset: correct UTC instant is 2026-01-01T00:00:00Z (one day earlier wall-clock).
        var json = "\"2026-01-01T09:00:00+09:00\"";

        var result = JsonSerializer.Deserialize<DateTime>(json, Options);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Read_ZSuffix_PassesThroughUnchanged()
    {
        var json = "\"2026-01-01T10:00:00Z\"";

        var result = JsonSerializer.Deserialize<DateTime>(json, Options);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Read_BareDateNoOffset_IsRelabeledAsUtcWithoutShifting()
    {
        // Bare date-only value (e.g. from an HTML <input type="date">) has no offset to correct
        // for, so the wall-clock digits must be preserved exactly, just relabeled as Utc.
        var json = "\"2026-11-20\"";

        var result = JsonSerializer.Deserialize<DateTime>(json, Options);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
    }
}
