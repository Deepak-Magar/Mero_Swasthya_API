using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 facilities, code lists, rules, config.</summary>
[Collection(ApiCollection.Name)]
public sealed class CatalogTests(ApiFactory factory)
{
    private static readonly string[] FacilityKeys =
        ["id", "name", "type", "hasBirthingCentre", "phone", "lat", "lng", "municipality", "distanceKm"];

    private readonly ApiClient _api = new(factory.CreateClient());

    // ---- GET /rules

    [Fact]
    public async Task Rules_deep_equal_the_apps_assets_rules_json()
    {
        var (expected, source) = AppAssets.Load("rules.json");
        var data = (await _api.Get("/rules")).Data();

        JsonAssert.SemanticEquals(data, expected).Should().BeTrue($"GET /rules must equal {source} verbatim");
        data["version"]!.GetValue<string>().Should().Be("2026-09-18.1");
        data["ancSchedule"]!.AsArray().Select(c => c!["weekTarget"]!.GetValue<int>())
            .Should().Equal(12, 20, 26, 30, 34, 36, 38, 40);
    }

    // ---- GET /codelists

    [Fact]
    public async Task Codelists_match_the_apps_assets_codelists_json_exactly()
    {
        var (expected, source) = AppAssets.Load("codelists.json");
        var data = (await _api.Get("/codelists")).Data();

        JsonAssert.HasExactKeys(data, "version", "items");
        data["version"]!.GetValue<string>().Should().Be(expected["version"]!.GetValue<string>());
        JsonAssert.SemanticEquals(data["items"], expected["items"]).Should().BeTrue($"items must equal {source} (same codes, labels, meta, order)");
    }

    [Fact]
    public async Task Codelists_have_the_minimum_counts_and_the_part_A_drug_example()
    {
        var items = (await _api.Get("/codelists")).Data()["items"]!.AsArray();
        int Count(string kind) => items.Count(i => i!["kind"]!.GetValue<string>() == kind);

        Count("complaint").Should().BeGreaterThanOrEqualTo(30);
        Count("diagnosis").Should().BeGreaterThanOrEqualTo(40);
        Count("drug").Should().BeGreaterThanOrEqualTo(30);

        var metformin = items.Single(i => i!["code"]!.GetValue<string>() == "METFORMIN_500");
        JsonAssert.DeepEqual(metformin, Json.Obj("""
            {
              "kind": "drug",
              "code": "METFORMIN_500",
              "labelEn": "Metformin 500 mg",
              "labelNp": "मेटफर्मिन ५०० मि.ग्रा.",
              "meta": { "strength": "500 mg", "form": "tablet" }
            }
            """));
        items.Single(i => i!["code"]!.GetValue<string>() == "CC_FEVER")!["meta"].Should().BeNull();
    }

    [Theory]
    [InlineData("drug")]
    [InlineData("dangerSign")]
    [InlineData("riskFactor")]
    public async Task Codelists_filter_by_kind(string kind)
    {
        var items = (await _api.Get($"/codelists?kind={kind}")).Data()["items"]!.AsArray();
        items.Should().NotBeEmpty();
        items.Should().OnlyContain(i => i!["kind"]!.GetValue<string>() == kind);
    }

    [Fact]
    public async Task Codelists_reject_an_unknown_kind()
    {
        (await _api.Get("/codelists?kind=vaccine")).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey("kind").Should().BeTrue();
    }

    // ---- GET /config

    [Fact]
    public async Task Config_returns_the_flags()
    {
        var data = (await _api.Get("/config")).Data();
        JsonAssert.DeepEqual(data, Json.Obj("""
            {
              "smsMode": "mock",
              "aiSummaryEnabled": false,
              "otpDemo": true,
              "rulesVersion": "2026-09-18.1",
              "codelistVersion": "2026-09-18.1",
              "nidEnabled": false,
              "hmisExportEnabled": false,
              "councilVerifyEnabled": false
            }
            """));
    }

    [Theory]
    [InlineData("/codelists")]
    [InlineData("/rules")]
    [InlineData("/config")]
    public async Task Reference_endpoints_need_no_auth_even_with_a_stale_token(string path)
    {
        (await _api.Get(path, "stale.token.value")).Data();
    }

    // ---- GET /facilities/nearby

    [Fact]
    public async Task Nearby_orders_by_haversine_distance_and_sets_distanceKm()
    {
        var session = await TestUsers.Login(_api, TestUsers.ProviderPhone);
        var items = (await _api.Get("/facilities/nearby?lat=28.03&lng=82.49&limit=5", session.AccessToken))
            .Data()["items"]!.AsArray();

        items.Select(i => i!["id"]!.GetValue<string>()).Should().Equal("f_0001", "f_0002", "f_0004", "f_0003");
        foreach (var item in items) JsonAssert.HasExactKeys(item, FacilityKeys);
        var distances = items.Select(i => i!["distanceKm"]!.GetValue<double>()).ToList();
        distances.Should().BeInAscendingOrder();
        // Reference values from an independent Python Haversine (R = 6371.0088 km), rounded to 0.1 km.
        distances[0].Should().Be(0.5);    // Ghorahi HP (0.456 km)
        distances[1].Should().Be(0.6);    // Rapti Provincial Hospital (0.576 km)
        distances[2].Should().Be(18.6);   // Lamahi Birthing Centre (18.644 km)
        distances[3].Should().Be(22.0);   // Tulsipur PHCC (21.958 km)
    }

    [Fact]
    public async Task Nearby_matches_the_part_A_facility_example_shape()
    {
        var session = await TestUsers.Login(_api, TestUsers.ProviderPhone);
        var items = (await _api.Get("/facilities/nearby?lat=28.0345&lng=82.4871&limit=1", session.AccessToken))
            .Data()["items"]!.AsArray();

        items.Should().HaveCount(1);
        JsonAssert.DeepEqual(items[0], Json.Obj("""
            {
              "id": "f_0002",
              "name": "Rapti Provincial Hospital",
              "type": "hospital",
              "hasBirthingCentre": true,
              "phone": "+97782560000",
              "lat": 28.0345,
              "lng": 82.4871,
              "municipality": "Tulsipur",
              "distanceKm": 0.0
            }
            """));
    }

    [Fact]
    public async Task Nearby_respects_birthing_and_limit()
    {
        var session = await TestUsers.Login(_api, TestUsers.ProviderPhone);
        var items = (await _api.Get("/facilities/nearby?lat=27.87&lng=82.54&birthing=true&limit=2", session.AccessToken))
            .Data()["items"]!.AsArray();

        items.Should().HaveCount(2);
        items[0]!["id"]!.GetValue<string>().Should().Be("f_0004");
        items.Should().OnlyContain(i => i!["hasBirthingCentre"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Nearby_validates_its_query()
    {
        var session = await TestUsers.Login(_api, TestUsers.ProviderPhone);
        var details = (await _api.Get("/facilities/nearby?lat=abc&limit=0&birthing=maybe", session.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.Select(d => d.Key).Should().BeEquivalentTo(["lat", "lng", "birthing", "limit"]);
    }

    [Fact]
    public async Task Nearby_requires_auth()
    {
        (await _api.Get("/facilities/nearby?lat=28&lng=82")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Facilities_lists_the_four_dang_facilities_with_null_distance()
    {
        var session = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var items = (await _api.Get("/facilities", session.AccessToken)).Data()["items"]!.AsArray();

        var (expected, _) = AppAssets.Load("facilities.json");
        JsonAssert.SemanticEquals(items, expected["items"]).Should().BeTrue(items.ToJsonString());
    }
}
