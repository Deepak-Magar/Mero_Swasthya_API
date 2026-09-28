using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeroSwasthya.Shared.Json;

/// <summary>
/// The single JSON configuration for the whole API (A.1): camelCase keys, nulls always written,
/// DateOnly as <c>YYYY-MM-DD</c>, DateTime as ISO-8601 UTC with milliseconds, enums as the Part A
/// strings. Applied to minimal-API request/response serialisation and reused by tests.
/// </summary>
public static class JsonDefaults
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null; // detail keys are already field paths ("birthPlan.facilityId")
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.PropertyNameCaseInsensitive = false;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // "+977…" and Devanagari stay readable
        options.Converters.Add(new DateOnlyJsonConverter());
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new WireEnumConverterFactory());
        options.Converters.Add(new OptionalJsonConverterFactory());
    }

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }

    public static readonly JsonSerializerOptions Options = Create();
}
