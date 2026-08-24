using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnicoreCRM.ApiHost.Serialization;

/// <summary>
/// Transport-boundary policy for the canonical UtcDateTime wire contract, which
/// requires an ISO-8601 instant ending in the literal UTC designator Z. The
/// System.Text.Json default writes a numeric offset such as +00:00 for every
/// DateTimeOffset, which does not satisfy that contract.
/// </summary>
/// <remarks>
/// Writing normalizes the represented instant to UTC through
/// <see cref="DateTimeOffset.UtcDateTime"/> rather than rewriting the textual
/// suffix, so a value carrying a non-zero offset is converted to the equivalent
/// UTC instant. The emitted precision matches the format the owner projections
/// already use, keeping one wire shape across every owner. Reading delegates to
/// the native System.Text.Json parser so accepted request formats are unchanged.
/// No <c>DateTime</c> converter is registered because no canonical wire contract
/// in this backend exposes a bare <c>DateTime</c>; every date-time field is a
/// <see cref="DateTimeOffset"/> or a <see cref="DateOnly"/> business date.
/// </remarks>
internal sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string UtcInstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(UtcInstantFormat, CultureInfo.InvariantCulture));
}
