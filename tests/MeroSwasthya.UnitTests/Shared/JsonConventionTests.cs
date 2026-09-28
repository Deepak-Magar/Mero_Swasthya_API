using System.Text.Json;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;

namespace MeroSwasthya.UnitTests.Shared;

public sealed class JsonConventionTests
{
    private enum FacilityKind
    {
        [WireName("health_post")] HealthPost,
        [WireName("birthing_centre")] BirthingCentre,
        DangerSign,
    }

    private sealed record Sample(
        string SomeField, DateOnly Dob, DateTime UpdatedAt, FacilityKind Type, string? Nullable, UserRole Role);

    private sealed class Patch
    {
        public Optional<string> BloodGroup { get; init; }
        public Optional<int?> Ward { get; init; }
        public Optional<List<string>> Allergies { get; init; }
    }

    private static readonly JsonSerializerOptions Options = JsonDefaults.Create();

    [Fact]
    public void Writes_camelCase_dates_utc_ms_and_part_a_enum_strings()
    {
        var sample = new Sample("x", new DateOnly(2002, 4, 11),
            new DateTime(2026, 9, 18, 4, 5, 0, 7, DateTimeKind.Utc), FacilityKind.HealthPost, null, UserRole.Fchv);

        JsonSerializer.Serialize(sample, Options).Should().Be(
            "{\"someField\":\"x\",\"dob\":\"2002-04-11\",\"updatedAt\":\"2026-09-18T04:05:00.007Z\"," +
            "\"type\":\"health_post\",\"nullable\":null,\"role\":\"fchv\"}");
    }

    [Fact]
    public void Enums_without_an_attribute_are_camelCase()
    {
        JsonSerializer.Serialize(FacilityKind.DangerSign, Options).Should().Be("\"dangerSign\"");
        JsonSerializer.Serialize(FacilityKind.BirthingCentre, Options).Should().Be("\"birthing_centre\"");
    }

    [Fact]
    public void Non_utc_timestamps_are_normalised_to_utc()
    {
        var parsed = JsonSerializer.Deserialize<DateTime>("\"2026-09-18T09:50:00.000+05:45\"", Options);
        parsed.Kind.Should().Be(DateTimeKind.Utc);
        JsonSerializer.Serialize(parsed, Options).Should().Be("\"2026-09-18T04:05:00.000Z\"");
    }

    [Theory]
    [InlineData("\"2026-09-18T04:05:00\"")] // no zone: ambiguous
    [InlineData("\"yesterday\"")]
    [InlineData("12")]
    public void Rejects_ambiguous_or_malformed_timestamps(string json)
    {
        var act = () => JsonSerializer.Deserialize<DateTime>(json, Options);
        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"2002-13-01\"")]
    [InlineData("\"11/04/2002\"")]
    [InlineData("\"2002-04-11T00:00:00Z\"")]
    public void Rejects_non_calendar_dates(string json)
    {
        var act = () => JsonSerializer.Deserialize<DateOnly>(json, Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Unknown_enum_strings_are_rejected()
    {
        var act = () => JsonSerializer.Deserialize<UserRole>("\"doctor\"", Options);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Optional_distinguishes_omitted_from_explicit_null()
    {
        var patch = JsonSerializer.Deserialize<Patch>("{\"bloodGroup\":null,\"allergies\":[\"sulpha\"]}", Options)!;

        patch.BloodGroup.HasValue.Should().BeTrue();
        patch.BloodGroup.Value.Should().BeNull();
        patch.Ward.HasValue.Should().BeFalse();
        patch.Allergies.Value.Should().Equal("sulpha");
    }

    [Fact]
    public void Nepali_text_and_plus_signs_are_not_escaped()
    {
        JsonSerializer.Serialize(new { phone = "+9779801000001", np = "मधुमेह" }, Options)
            .Should().Be("{\"phone\":\"+9779801000001\",\"np\":\"मधुमेह\"}");
    }
}
