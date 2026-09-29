using System.Text.Json.Nodes;
using MeroSwasthya.Api.Hosting;
using MeroSwasthya.ContractTests.Infrastructure;
using MeroSwasthya.Shared.Ids;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.ContractTests;

/// <summary>The seeded demo pregnancy: Sita at week 30 with contacts 1–3 done (BACKEND_PROGRESS §5.1, mock_api.dart).</summary>
[Collection(ApiCollection.Name)]
public sealed class MaternalSeedTests
{
    private const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    private const string PregnancyId = "pg_b2b2b2b2-0000-4000-8000-000000000001";

    private readonly ApiFactory _factory;
    private readonly ApiClient _api;
    private readonly Scenario _s;

    public MaternalSeedTests(ApiFactory factory)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    [Fact]
    public async Task Sita_is_at_week_30_with_contacts_1_to_3_recorded_and_contact_4_next()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var data = (await _api.Get($"/pregnancies/{PregnancyId}", owner.AccessToken)).Data();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lmp = today.AddDays(-210);

        var pregnancy = data["pregnancy"]!;
        JsonAssert.HasExactKeys(pregnancy, PregnanciesTests.PregnancyKeys);
        pregnancy["id"]!.GetValue<string>().Should().Be(PregnancyId);
        pregnancy["patientId"]!.GetValue<string>().Should().Be(SitaId);
        pregnancy["lmp"]!.GetValue<string>().Should().Be(lmp.ToString("yyyy-MM-dd"));
        pregnancy["edd"]!.GetValue<string>().Should().Be(lmp.AddDays(280).ToString("yyyy-MM-dd"));
        pregnancy["gestationalAgeDays"]!.GetValue<int>().Should().Be(210);
        pregnancy["status"]!.GetValue<string>().Should().Be("active");
        pregnancy["riskLevel"]!.GetValue<string>().Should().Be("normal");
        pregnancy["registeredByUserId"]!.GetValue<string>().Should().Be(TestUsers.ProviderId);
        pregnancy["nextContact"]!["contactNo"]!.GetValue<int>().Should().Be(4);
        pregnancy["nextContact"]!["dueAt"]!.GetValue<string>().Should().Be(today.ToString("yyyy-MM-dd"), "week 30 is today");

        var contacts = data["ancContacts"]!.AsArray();
        contacts.Should().HaveCount(8);
        foreach (var c in contacts)
        {
            JsonAssert.HasExactKeys(c, PregnanciesTests.ContactKeys);
            c!["id"]!.GetValue<string>().Should().Be(Ids.AncContactId(PregnancyId, c["contactNo"]!.GetValue<int>()));
        }
        contacts.Select(c => c!["doneAt"] is null).Should().Equal(false, false, false, true, true, true, true, true);
        contacts.Take(3).Select(c => c!["triageLevel"]!.GetValue<string>()).Should().OnlyContain(l => l == "green");
        contacts.Take(3).Select(c => c!["providerUserId"]!.GetValue<string>()).Should().OnlyContain(id => id == TestUsers.ProviderId);
        contacts.Take(3).Select(c => c!["version"]!.GetValue<int>()).Should().OnlyContain(v => v == 2);
        JsonAssert.DeepEqual(contacts[0]!["findings"], Json.Obj("""
            { "weightKg": 52.0, "bpSys": 110, "bpDia": 70, "hbGdl": 11.8, "urineProtein": "neg", "tdDoseGiven": true, "ifaGiven": true }
            """));
        JsonAssert.DeepEqual(contacts[2]!["findings"], Json.Obj("""
            { "weightKg": 58.0, "bpSys": 124, "bpDia": 80, "fundalHeightCm": 26.0, "fhrBpm": 142, "ifaGiven": true, "calciumGiven": true, "fetalMovement": "normal" }
            """));
        contacts[2]!["doneAt"]!.GetValue<string>().Should().Be($"{today.AddDays(-28):yyyy-MM-dd}T04:05:00.000Z");
        data["delivery"].Should().BeNull();
    }

    [Fact]
    public async Task Sitas_summary_timeline_and_share_bundle_carry_the_seeded_pregnancy()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var summary = (await _api.Get($"/patients/{SitaId}", owner.AccessToken)).Data()["summary"]!;
        summary["activePregnancy"]!["id"]!.GetValue<string>().Should().Be(PregnancyId);

        var items = (await _api.Get($"/patients/{SitaId}/timeline?limit=50", owner.AccessToken)).Data()["items"]!.AsArray();
        var kinds = items.Select(i => i!["kind"]!.GetValue<string>()).ToList();
        kinds.Should().Contain("pregnancy_registered");
        kinds.Count(k => k == "anc_contact").Should().Be(3);
        items.Where(i => i!["kind"]!.GetValue<string>() == "anc_contact").Select(i => i!["title"]!.GetValue<string>())
            .Should().Equal("ANC contact 3 (week 26)", "ANC contact 2 (week 20)", "ANC contact 1 (week 12)");
        items.Where(i => i!["kind"]!.GetValue<string>() == "anc_contact").Select(i => i!["badge"]!.GetValue<string>())
            .Should().OnlyContain(b => b == "green");
        items.Single(i => i!["kind"]!.GetValue<string>() == "anc_contact" && i["title"]!.GetValue<string>().StartsWith("ANC contact 3"))!
            ["subtitle"]!.GetValue<string>().Should().Be("BP 124/80");

        var share = await _s.Share(new Family(owner, SitaId));
        var worker = await TestUsers.NewHealthWorker(_api);
        var bundle = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        bundle["pregnancy"]!["id"]!.GetValue<string>().Should().Be(PregnancyId);
        bundle["ancContacts"]!.AsArray().Should().HaveCount(8);
    }

    [Fact]
    public async Task Reseeding_does_not_duplicate_or_reset_the_pregnancy()
    {
        // The seed already ran at startup; running every module's seed again (what POST /dev/seed does) must be a no-op.
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var before = (await _api.Get($"/pregnancies/{PregnancyId}", owner.AccessToken)).Data();
        await DatabaseBootstrap.SeedAsync(_factory.Services, NullLogger.Instance);
        var after = (await _api.Get($"/pregnancies/{PregnancyId}", owner.AccessToken)).Data();
        JsonAssert.DeepEqual(after, before);
        var list = (await _api.Get($"/patients/{SitaId}/pregnancies", owner.AccessToken)).Data()["items"]!.AsArray();
        list.Should().HaveCount(1);
        JsonAssert.DeepEqual(list[0], before["pregnancy"]);
    }
}
