using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeroSwasthya.Shared.Json;

/// <summary>
/// The exact Part A string for an enum member, e.g. <c>[WireName("health_post")]</c>.
/// Members without the attribute are written camelCase.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class WireNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public static class WireEnum
{
    private static readonly ConcurrentDictionary<Type, (Dictionary<string, object> FromWire, Dictionary<object, string> ToWire)> Cache = new();

    private static (Dictionary<string, object> FromWire, Dictionary<object, string> ToWire) Maps(Type type) =>
        Cache.GetOrAdd(type, t =>
        {
            var from = new Dictionary<string, object>(StringComparer.Ordinal);
            var to = new Dictionary<object, string>();
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = field.GetValue(null)!;
                var name = field.GetCustomAttribute<WireNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(field.Name);
                from[name] = value;
                to[value] = name;
            }
            return (from, to);
        });

    public static string ToWire<T>(this T value) where T : struct, Enum => Maps(typeof(T)).ToWire[value];

    public static bool TryParse<T>(string? wire, out T value) where T : struct, Enum
    {
        if (wire is not null && Maps(typeof(T)).FromWire.TryGetValue(wire, out var boxed))
        {
            value = (T)boxed;
            return true;
        }
        value = default;
        return false;
    }

    public static T Parse<T>(string wire) where T : struct, Enum =>
        TryParse<T>(wire, out var value) ? value : throw new ArgumentException($"'{wire}' is not a {typeof(T).Name}");

    public static IReadOnlyCollection<string> Names<T>() where T : struct, Enum => Maps(typeof(T)).FromWire.Keys;

    internal static object? FromWire(Type type, string wire) =>
        Maps(type).FromWire.TryGetValue(wire, out var v) ? v : null;

    internal static string ToWire(Type type, object value) => Maps(type).ToWire[value];
}

public sealed class WireEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(WireEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class WireEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && WireEnum.FromWire(typeof(T), reader.GetString()!) is T value)
                return value;
            throw new JsonException($"Expected one of: {string.Join(", ", WireEnum.Names<T>())}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WireEnum.ToWire(typeof(T), value));
    }
}
