using MeroSwasthya.Modules.Catalog.Application;
using MeroSwasthya.Modules.Catalog.Endpoints;
using MeroSwasthya.Shared.Errors;

namespace MeroSwasthya.UnitTests.Catalog;

public sealed class CatalogUnitTests
{
    [Fact]
    public void Haversine_is_zero_for_the_same_point_and_symmetric()
    {
        Geo.HaversineKm(28.03, 82.49, 28.03, 82.49).Should().Be(0);
        Geo.HaversineKm(28.03, 82.49, 27.869, 82.543)
            .Should().BeApproximately(Geo.HaversineKm(27.869, 82.543, 28.03, 82.49), 1e-9);
    }

    [Fact]
    public void Haversine_matches_a_known_distance()
    {
        // Kathmandu (27.7172, 85.3240) → Pokhara (28.2096, 83.9856): ≈ 141.6 km great-circle.
        Geo.HaversineKm(27.7172, 85.3240, 28.2096, 83.9856).Should().BeApproximately(141.6, 1.0);
    }

    [Fact]
    public void Rules_provider_exposes_the_embedded_document_and_version()
    {
        var rules = new RulesProvider();
        rules.Version.Should().Be("2026-09-18.1");
        rules.Document.GetProperty("ancSchedule").GetArrayLength().Should().Be(8);
        rules.Document.GetProperty("dangerSigns").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString())
            .Should().Contain(["SEVERE_HEADACHE_BLURRED_VISION", "SWELLING_FACE_HANDS"]);
        rules.Document.GetProperty("riskFactors").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString())
            .Should().Contain("PREV_CS");
    }

    [Fact]
    public void Nearby_query_defaults_and_bounds()
    {
        var q = CatalogEndpoints.ParseNearby("28.03", "82.49", null, null);
        q.Limit.Should().Be(5);
        q.BirthingOnly.Should().BeFalse();

        var act = () => CatalogEndpoints.ParseNearby("91", "181", "yes", "51");
        act.Should().Throw<AppException>().Which.Details.Should()
            .BeAssignableTo<IDictionary<string, string>>().Which.Keys.Should().BeEquivalentTo(["lat", "lng", "birthing", "limit"]);
    }
}
