using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeroSwasthya.Shared.Json;

/// <summary>
/// A.1: "In requests, omitted = unchanged (PATCH)". A PATCH field is <see cref="Optional{T}"/> so an
/// omitted key (<see cref="HasValue"/> false) is distinguishable from an explicit <c>null</c>
/// (<see cref="HasValue"/> true, <see cref="Value"/> null).
/// </summary>
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; }
    public T? Value { get; }

    public static Optional<T> Omitted => default;

    public T? Or(T? fallback) => HasValue ? Value : fallback;

    public override string ToString() => HasValue ? $"{Value}" : "<omitted>";
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(OptionalConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        // Called for explicit nulls too, so "field": null becomes Optional(null) rather than "omitted".
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return new Optional<T>(default);
            return new Optional<T>(JsonSerializer.Deserialize<T>(ref reader, options));
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.HasValue || value.Value is null) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
