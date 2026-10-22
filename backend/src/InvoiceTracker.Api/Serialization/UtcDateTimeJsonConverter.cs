using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvoiceTracker.Api.Serialization;

/// <summary>
/// Normalizes every JSON-body-bound <see cref="DateTime"/> (and, automatically, <see cref="Nullable{DateTime}"/>
/// via System.Text.Json's built-in nullable-wrapping of value-type converters) to <see cref="DateTimeKind.Utc"/>
/// on deserialization.
///
/// Why this exists: Npgsql (v6+) deliberately rejects writing/parameterizing a <c>Kind = Unspecified</c>
/// <see cref="DateTime"/> against a <c>timestamp with time zone</c> column, to avoid silently guessing a
/// timezone. A bare date-only JSON string (e.g. "2026-11-20" — exactly what an HTML
/// &lt;input type="date"&gt; produces) deserializes to <c>Kind = Unspecified</c> by default, so without this
/// converter any request DTO field like <c>CreateInvoiceRequest.DueDate</c> throws deep inside
/// EF Core/Npgsql, which <see cref="InvoiceTracker.Api.Middleware.ExceptionHandlingMiddleware"/> turns into
/// an unhelpful bare 500.
///
/// Registered once, globally (see Program.cs), so it covers every current and future DateTime field bound
/// from a JSON request body — not just invoice dates.
/// </summary>
public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();

        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        // An explicit non-"Z" UTC offset (e.g. "2026-11-20T00:00:00-05:00") deserializes to
        // Kind = Local, representing an already-shifted walltime. Convert the instant correctly via
        // ToUniversalTime() before relabeling — merely relabeling (SpecifyKind) without converting
        // would silently shift the represented instant by the server's local UTC offset. A bare
        // date-only value (Kind = Unspecified, no offset at all) has no shift to correct for, so it's
        // simply relabeled.
        if (value.Kind == DateTimeKind.Local)
        {
            return DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Preserve System.Text.Json's default DateTime serialization (ISO-8601 "O" round-trip format,
        // honoring the instance's Kind) — this converter only changes read behavior.
        writer.WriteStringValue(value);
    }
}
