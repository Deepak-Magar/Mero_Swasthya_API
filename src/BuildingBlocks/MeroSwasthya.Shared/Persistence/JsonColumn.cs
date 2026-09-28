using System.Text.Json;
using MeroSwasthya.Shared.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeroSwasthya.Shared.Persistence;

/// <summary>
/// Maps a value object (vitals, referral, prescriptions, findings) to a <c>jsonb</c> column using the
/// API's own JSON conventions, so what is stored is exactly what the wire shows.
/// </summary>
public static class JsonColumn
{
    public static PropertyBuilder<T> AsJsonb<T>(this PropertyBuilder<T> property)
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonDefaults.Options),
                s => JsonSerializer.Deserialize<T>(s, JsonDefaults.Options)!,
                new ValueComparer<T>(
                    (a, b) => JsonSerializer.Serialize(a, JsonDefaults.Options) == JsonSerializer.Serialize(b, JsonDefaults.Options),
                    v => JsonSerializer.Serialize(v, JsonDefaults.Options).GetHashCode(),
                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonDefaults.Options), JsonDefaults.Options)!));
        return property;
    }
}
