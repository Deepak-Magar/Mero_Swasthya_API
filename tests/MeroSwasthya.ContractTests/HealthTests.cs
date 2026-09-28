using System.Net;
using MeroSwasthya.ContractTests.Infrastructure;
using Xunit.Abstractions;

namespace MeroSwasthya.ContractTests;

[Collection(ApiCollection.Name)]
public sealed class HealthTests(ApiFactory factory, ITestOutputHelper output)
{
    private readonly ApiClient _api = new(factory.CreateClient());

    [Fact]
    public void Reports_which_database_the_contract_tests_use()
    {
        output.WriteLine($"Contract tests database: {factory.DatabaseMode}");
        output.WriteLine($"Contract tests document storage: {factory.StorageMode}");
        factory.DatabaseMode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Health_returns_the_success_envelope()
    {
        var data = (await _api.Get("/health")).Data();

        JsonAssert.HasExactKeys(data, "status", "service", "contractVersion", "serverTime", "database", "modules");
        data["status"]!.GetValue<string>().Should().Be("healthy");
        data["database"]!.GetValue<string>().Should().Be("up");
        JsonAssert.IsIsoTimestamp(data["serverTime"]);
        data["modules"]!.AsArray().Select(m => m!.GetValue<string>()).Should()
            .BeEquivalentTo(["catalog", "auth", "patients", "audit", "grants", "clinical", "maternal", "reminders", "sync"]);
    }

    [Fact]
    public async Task Unknown_route_returns_not_found_envelope()
    {
        (await _api.Get("/no/such/route")).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
