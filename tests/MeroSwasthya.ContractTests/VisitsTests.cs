using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 "Visits" + the summary/timeline Clinical contributes to GET /patients/:id and /timeline.</summary>
[Collection(ApiCollection.Name)]
public sealed class VisitsTests
{
    public static readonly string[] VisitKeys =
    [
        "id", "patientId", "providerUserId", "providerName", "facilityId", "facilityName", "visitAt",
        "chiefComplaintCode", "vitals", "diagnosisCodes", "notes", "advice", "followUpAt", "referral",
        "prescriptions", "supersedesId", "version", "updatedAt", "deleted",
    ];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public VisitsTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    private Task<ApiResponse> AddVisit(Family family, JsonObject body, Session who) =>
        _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", body, who.AccessToken);

    [Fact]
    public async Task Provider_visit_matches_the_part_A_example()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var id = $"v_{Guid.NewGuid()}";

        var data = (await AddVisit(family, Scenario.VisitBody(id), worker)).Data();

        JsonAssert.HasExactKeys(data, "visit");
        JsonAssert.HasExactKeys(data["visit"], VisitKeys);
        JsonAssert.DeepEqual(data["visit"], Json.Obj($$"""
            {
              "id": "{{id}}",
              "patientId": "{{family.PatientId}}",
              "providerUserId": "{{worker.UserId}}",
              "providerName": "Test Health Worker",
              "facilityId": "f_0001",
              "facilityName": "Ghorahi Health Post",
              "visitAt": "2026-09-18T04:05:00.000Z",
              "chiefComplaintCode": "CC_POLYURIA",
              "vitals": { "bpSys": 138, "bpDia": 88, "pulse": 76, "weightKg": 71.5 },
              "diagnosisCodes": ["E11"],
              "notes": "Fasting sugar 168 mg/dl",
              "advice": "Diet, walk 30 min daily",
              "followUpAt": "2026-10-18",
              "referral": null,
              "prescriptions": [
                {
                  "id": "rx_0001",
                  "drugCode": "METFORMIN_500",
                  "drugName": "Metformin 500 mg",
                  "dose": "1 tab",
                  "frequency": "BD",
                  "durationDays": 30,
                  "instructionsNp": "खाना पछि"
                }
              ],
              "supersedesId": null,
              "version": 1,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt");
    }

    [Fact]
    public async Task Create_is_idempotent_on_the_client_id()
    {
        var family = await _s.NewFamily();
        var body = Scenario.VisitBody();
        var first = (await AddVisit(family, body, family.Owner)).Data()["visit"];
        body["notes"] = "changed on retry";
        var second = (await AddVisit(family, body, family.Owner)).Data()["visit"];

        JsonAssert.DeepEqual(second, first);
        (await _api.Get($"/patients/{family.PatientId}/visits", family.Owner.AccessToken)).Data()["items"]!.AsArray()
            .Should().HaveCount(1, "no duplicate row");
        (await _s.AuditActions(family)).Count(a => a == "visit_added").Should().Be(1);
    }

    [Fact]
    public async Task Owner_visit_is_self_reported()
    {
        var family = await _s.NewFamily();
        var visit = (await AddVisit(family, Scenario.VisitBody(), family.Owner)).Data()["visit"]!;
        visit["providerName"]!.GetValue<string>().Should().Be("Self-reported");
        visit["providerUserId"]!.GetValue<string>().Should().Be(family.Owner.UserId);
        visit["facilityId"].Should().BeNull();
        visit["facilityName"].Should().BeNull();
    }

    [Fact]
    public async Task Referral_round_trips_and_omitted_vitals_are_omitted()
    {
        var family = await _s.NewFamily();
        var body = Scenario.VisitBody();
        body["vitals"] = Json.Obj("""{ "tempC": 38.6 }""");
        body["referral"] = Json.Obj("""{ "facilityName": "Rapti Provincial Hospital", "reason": "Needs X-ray", "urgency": "routine" }""");

        var visit = (await AddVisit(family, body, family.Owner)).Data()["visit"]!;
        JsonAssert.DeepEqual(visit["vitals"], Json.Obj("""{ "tempC": 38.6 }"""));
        JsonAssert.DeepEqual(visit["referral"], Json.Obj("""{ "facilityName": "Rapti Provincial Hospital", "reason": "Needs X-ray", "urgency": "routine" }"""));
    }

    [Theory]
    [InlineData("chiefComplaintCode", "\"CC_NOPE\"", "chiefComplaintCode")]
    [InlineData("diagnosisCodes", "[\"E11\", \"Z99.9X\"]", "diagnosisCodes")]
    [InlineData("vitals", "{ \"bpSys\": 900 }", "vitals.bpSys")]
    [InlineData("vitals", "{ \"spo2\": \"high\" }", "vitals.spo2")]
    [InlineData("visitAt", "\"2026-09-18 04:05\"", "visitAt")]
    [InlineData("referral", "{ \"facilityName\": \"Rapti\", \"urgency\": \"soon\" }", "referral.reason")]
    [InlineData("prescriptions", "[{ \"id\": \"rx_1\", \"drugCode\": \"NOT_A_DRUG\", \"dose\": \"1\", \"frequency\": \"BD\", \"durationDays\": 3 }]", "prescriptions[0].drugCode")]
    [InlineData("prescriptions", "[{ \"id\": \"rx_1\", \"drugCode\": \"METFORMIN_500\", \"dose\": \"1\", \"frequency\": \"TWICE\", \"durationDays\": 3 }]", "prescriptions[0].frequency")]
    [InlineData("prescriptions", "[{ \"id\": \"rx_1\", \"drugCode\": \"METFORMIN_500\", \"dose\": \"1\", \"frequency\": \"BD\", \"durationDays\": 0 }]", "prescriptions[0].durationDays")]
    [InlineData("notes", "\"" + "x" + "\"", null)]
    public async Task Codes_and_fields_are_validated_against_the_catalog(string field, string json, string? detailKey)
    {
        var family = await _s.NewFamily();
        var body = Scenario.VisitBody();
        body[field] = JsonNode.Parse(json);
        var response = await AddVisit(family, body, family.Owner);
        if (detailKey is null) { response.Data(); return; }

        response.Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey(detailKey).Should()
            .BeTrue(response.Raw);
    }

    [Fact]
    public async Task Fchv_cannot_record_a_visit_even_with_an_append_grant()
    {
        var family = await _s.NewFamily();
        var fchv = await _s.WorkerWithGrant(family, invite: "FCHV-W5-01");
        (await AddVisit(family, Scenario.VisitBody(), fchv)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Provider_without_a_grant_is_forbidden()
    {
        var family = await _s.NewFamily();
        var worker = await TestUsers.NewHealthWorker(_api);
        (await AddVisit(family, Scenario.VisitBody(), worker)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/patients/{family.PatientId}/visits", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task A_correction_supersedes_an_existing_visit_of_the_same_patient()
    {
        var family = await _s.NewFamily();
        var original = (await AddVisit(family, Scenario.VisitBody(), family.Owner)).Data()["visit"]!;
        var fix = Scenario.VisitBody(visitAt: "2026-09-18T05:00:00.000Z");
        fix["supersedesId"] = original["id"]!.GetValue<string>();
        (await AddVisit(family, fix, family.Owner)).Data()["visit"]!["supersedesId"]!.GetValue<string>()
            .Should().Be(original["id"]!.GetValue<string>());

        var dangling = Scenario.VisitBody();
        dangling["supersedesId"] = "v_does_not_exist";
        (await AddVisit(family, dangling, family.Owner)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey("supersedesId").Should().BeTrue();

        // The superseded visit no longer counts in the summary.
        (await _api.Get($"/patients/{family.PatientId}", family.Owner.AccessToken)).Data()["summary"]!["visitCount"]!
            .GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task There_is_no_way_to_edit_or_delete_a_visit()
    {
        var family = await _s.NewFamily();
        var visit = (await AddVisit(family, Scenario.VisitBody(), family.Owner)).Data()["visit"]!;
        var path = $"/patients/{family.PatientId}/visits/{visit["id"]}";
        (await _api.Send(HttpMethod.Put, path, new { }, family.Owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
        (await _api.Send(HttpMethod.Delete, path, null, family.Owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task List_is_newest_first()
    {
        var family = await _s.NewFamily();
        foreach (var at in new[] { "2026-05-01T04:00:00.000Z", "2026-09-01T04:00:00.000Z", "2026-07-01T04:00:00.000Z" })
            (await AddVisit(family, Scenario.VisitBody(visitAt: at), family.Owner)).Data();

        var data = (await _api.Get($"/patients/{family.PatientId}/visits?limit=50", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(data, "items");
        data["items"]!.AsArray().Select(v => v!["visitAt"]!.GetValue<string>()).Should()
            .Equal("2026-09-01T04:00:00.000Z", "2026-07-01T04:00:00.000Z", "2026-05-01T04:00:00.000Z");
        foreach (var v in data["items"]!.AsArray()) JsonAssert.HasExactKeys(v, VisitKeys);
    }

    // ---- Summary + timeline contributions

    [Fact]
    public async Task Summary_reflects_visits_problems_medicines_and_vitals()
    {
        var family = await _s.NewFamily();
        var today = DateTime.UtcNow.Date;
        string At(int daysAgo) => today.AddDays(-daysAgo).AddHours(4).ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'");

        var old = Scenario.VisitBody(visitAt: At(200));
        old["diagnosisCodes"] = new JsonArray("I10");
        old["prescriptions"] = JsonNode.Parse("""[{ "id": "rx_a", "drugCode": "AMLODIPINE_5", "dose": "1 tab", "frequency": "OD", "durationDays": 30 }]""");
        (await AddVisit(family, old, family.Owner)).Data();

        var recent = Scenario.VisitBody(visitAt: At(10));
        recent["followUpAt"] = null;
        recent["vitals"] = Json.Obj("""{ "pulse": 80 }""");
        (await AddVisit(family, recent, family.Owner)).Data();

        var summary = (await _api.Get($"/patients/{family.PatientId}", family.Owner.AccessToken)).Data()["summary"]!;
        JsonAssert.DeepEqual(summary, Json.Obj($$"""
            {
              "activeProblems": [
                { "code": "I10", "labelEn": "Essential hypertension", "labelNp": "उच्च रक्तचाप", "since": "{{At(200)[..10]}}" },
                { "code": "E11", "labelEn": "Type 2 diabetes", "labelNp": "मधुमेह", "since": "{{At(10)[..10]}}" }
              ],
              "currentMedicines": [
                { "id": "rx_0001", "drugCode": "METFORMIN_500", "drugName": "Metformin 500 mg", "dose": "1 tab",
                  "frequency": "BD", "durationDays": 30, "instructionsNp": "खाना पछि" }
              ],
              "allergies": ["sulpha"],
              "lastVitals": { "bpSys": 138, "bpDia": 88, "weightKg": 71.5, "at": "{{At(200)}}" },
              "activePregnancy": null,
              "lastVisitAt": "{{At(10)}}",
              "visitCount": 2
            }
            """));
    }

    [Fact]
    public async Task Timeline_items_for_visits_have_the_part_A_shape_and_page()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var first = (await AddVisit(family, Scenario.VisitBody(visitAt: "2026-08-01T04:05:00.000Z"), worker)).Data()["visit"]!;
        var second = Scenario.VisitBody(visitAt: "2026-09-01T04:05:00.000Z");
        second["diagnosisCodes"] = new JsonArray();
        second["vitals"] = new JsonObject();
        var secondVisit = (await AddVisit(family, second, worker)).Data()["visit"]!;

        var page1 = (await _api.Get($"/patients/{family.PatientId}/timeline?limit=1", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(page1, "items", "nextBefore");
        page1["nextBefore"]!.GetValue<string>().Should().Be("2026-09-01T04:05:00.000Z");
        var item = page1["items"]!.AsArray().Single()!;
        JsonAssert.HasExactKeys(item, "kind", "at", "title", "subtitle", "badge", "refId", "payload");
        JsonAssert.DeepEqual(item, new JsonObject
        {
            ["kind"] = "visit",
            ["at"] = "2026-09-01T04:05:00.000Z",
            ["title"] = "Visit — Ghorahi Health Post — Frequent urination",
            ["subtitle"] = "Frequent urination",
            ["badge"] = null,
            ["refId"] = secondVisit["id"]!.GetValue<string>(),
            ["payload"] = secondVisit.DeepClone(),
        });

        var page2 = (await _api.Get($"/patients/{family.PatientId}/timeline?limit=1&before=2026-09-01T04:05:00.000Z",
            family.Owner.AccessToken)).Data();
        var older = page2["items"]!.AsArray().Single()!;
        older["title"]!.GetValue<string>().Should().Be("Visit — Ghorahi Health Post — Type 2 diabetes");
        older["subtitle"]!.GetValue<string>().Should().Be("Frequent urination · BP 138/88");
        JsonAssert.DeepEqual(older["payload"], first);
        page2["nextBefore"].Should().BeNull();
    }

    [Fact]
    public async Task Seeded_history_is_there_for_Aarav_and_Sita()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var aarav = (await _api.Get("/patients/p_a1a1a1a1-0000-4000-8000-000000000006/visits", owner.AccessToken)).Data()["items"]!.AsArray();
        aarav.Select(v => v!["chiefComplaintCode"]!.GetValue<string>()).Should().Equal("CC_COUGH", "CC_DIARRHOEA");
        aarav[1]!["prescriptions"]!.AsArray().Select(p => p!["drugCode"]!.GetValue<string>()).Should().Equal("ORS_SACHET", "ZINC_20");

        var sita = (await _api.Get("/patients/p_a1a1a1a1-0000-4000-8000-000000000001/visits", owner.AccessToken)).Data()["items"]!.AsArray();
        sita.Single()!["diagnosisCodes"]!.AsArray().Select(d => d!.GetValue<string>()).Should().Equal("R50");
    }
}
