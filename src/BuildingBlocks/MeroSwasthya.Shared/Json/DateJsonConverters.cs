using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeroSwasthya.Shared.Json;

/// <summary>Calendar dates (dob, lmp, edd, dueAt, followUpAt, takenAt) are <c>YYYY-MM-DD</c> in AD.</summary>
public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            DateOnly.TryParseExact(reader.GetString(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        throw new JsonException("Expected a calendar date in YYYY-MM-DD format.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>
/// Timestamps are ISO 8601 in UTC with milliseconds, e.g. <c>2026-09-18T04:05:00.000Z</c>.
/// Incoming values may carry any offset; they are normalised to UTC.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && TryParse(reader.GetString(), out var value))
            return value;
        throw new JsonException("Expected an ISO-8601 timestamp.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToWire(value));

    public static string ToWire(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };
        return utc.ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>Requires an explicit offset or <c>Z</c>: a timestamp without one is ambiguous.</summary>
    public static bool TryParse(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length < 11) return false;
        var hasZone = text.EndsWith('Z') || text.EndsWith('z') ||
                      System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
        if (!hasZone) return false;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return false;
        value = dto.UtcDateTime;
        return true;
    }
}
