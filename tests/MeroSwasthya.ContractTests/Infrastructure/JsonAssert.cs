using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeroSwasthya.ContractTests.Infrastructure;

public static partial class JsonAssert
{
    /// <summary>The object has exactly these keys — no more (no leaked columns), no fewer (nullable = present as null).</summary>
    public static void HasExactKeys(JsonNode? node, params string[] keys)
    {
        var obj = node.Should().BeOfType<JsonObject>().Subject;
        obj.Select(p => p.Key).Should().BeEquivalentTo(keys, $"shape of {obj.ToJsonString()}");
    }

    /// <summary>Deep equality after replacing the listed keys (server-assigned values) in <paramref name="actual"/>.</summary>
    public static void DeepEqual(JsonNode? actual, JsonNode? expected, params string[] ignoreKeys)
    {
        var a = actual?.DeepClone();
        var e = expected?.DeepClone();
        foreach (var key in ignoreKeys)
        {
            if (a is JsonObject ao && ao.ContainsKey(key)) ao[key] = "<ignored>";
            if (e is JsonObject eo && eo.ContainsKey(key)) eo[key] = "<ignored>";
        }
        SemanticEquals(a, e).Should().BeTrue($"expected {e?.ToJsonString()}\n   but got {a?.ToJsonString()}");
    }

    /// <summary>JSON equality: object key order ignored, numbers compared by value (0 == 0.0), strings ordinal.</summary>
    public static bool SemanticEquals(JsonNode? a, JsonNode? b)
    {
        switch (a, b)
        {
            case (null, null):
                return true;
            case (JsonObject oa, JsonObject ob):
                return oa.Count == ob.Count && oa.All(p => ob.ContainsKey(p.Key) && SemanticEquals(p.Value, ob[p.Key]));
            case (JsonArray xa, JsonArray xb):
                return xa.Count == xb.Count && xa.Zip(xb).All(z => SemanticEquals(z.First, z.Second));
            case (JsonValue va, JsonValue vb):
                var ka = va.GetValueKind();
                if (ka != vb.GetValueKind()) return false;
                return ka == System.Text.Json.JsonValueKind.Number
                    ? decimal.Parse(va.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
                      == decimal.Parse(vb.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
                    : va.ToJsonString() == vb.ToJsonString();
            default:
                return false;
        }
    }

    /// <summary>A.1: ISO 8601 UTC with milliseconds, e.g. 2026-09-18T04:05:00.000Z.</summary>
    public static void IsIsoTimestamp(JsonNode? node)
    {
        var text = node!.GetValue<string>();
        IsoMs().IsMatch(text).Should().BeTrue($"'{text}' must look like 2026-09-18T04:05:00.000Z");
    }

    public static void IsCalendarDate(JsonNode? node)
    {
        var text = node!.GetValue<string>();
        Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}$").Should().BeTrue($"'{text}' must be YYYY-MM-DD");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$")]
    private static partial Regex IsoMs();
}
