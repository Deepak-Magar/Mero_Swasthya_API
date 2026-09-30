using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;
using MeroSwasthya.Shared.Ids;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 "Maternal" — pregnancies: register (with the 8 ANC contacts), read, list, patch.</summary>
[Collection(ApiCollection.Name)]
public sealed class PregnanciesTests
{
    public static readonly string[] PregnancyKeys =
    [
        "id", "patientId", "lmp", "edd", "gravida", "para", "riskFactors", "riskLevel", "status", "birthPlan",
        "registeredByUserId", "gestationalAgeDays", "nextContact", "version", "updatedAt", "deleted",
    ];

    public static readonly string[] ContactKeys =
    [
        "id", "pregnancyId", "contactNo", "weekTarget", "dueAt", "doneAt", "providerUserId", "findings", "dangerSigns",
        "triageLevel", "triageReasons", "referral", "version", "updatedAt", "deleted",
    ];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public PregnanciesTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    /// <summary>The A.4 example body with a fresh id; LMP 200 days ago so the pregnancy is current whenever the test runs.</summary>
    public static JsonObject Body(string? id = null, DateOnly? lmp = null)
    {
        var start = lmp ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-200);
        return Json.Obj($$"""
            {
              "id": "{{id ?? $"pg_{Guid.NewGuid()}"}}",
              "lmp": "{{start:yyyy-MM-dd}}",
              "edd": null,
              "gravida": 1,
              "para": 0,
              "riskFactors": [],
              "birthPlan": null
            }
            """);
    }

    private Task<ApiResponse> Register(Family family, JsonObject body, Session who) =>
        _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", body, who.AccessToken);

    [Fact]
    public async Task Register_returns_the_pregnancy_and_eight_empty_contacts_in_the_part_A_shape()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var id = $"pg_{Guid.NewGuid()}";
        var lmp = new DateOnly(2026, 2, 20);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var data = (await Register(family, Body(id, lmp), worker)).Data();
        JsonAssert.HasExactKeys(data, "pregnancy", "ancContacts");
        var pregnancy = data["pregnancy"]!;
        JsonAssert.HasExactKeys(pregnancy, PregnancyKeys);

        var contacts = data["ancContacts"]!.AsArray();
        contacts.Should().HaveCount(8);
        JsonAssert.DeepEqual(pregnancy, Json.Obj($$"""
            {
              "id": "{{id}}",
              "patientId": "{{family.PatientId}}",
              "lmp": "2026-02-20",
              "edd": "2026-11-27",
              "gravida": 1,
              "para": 0,
              "riskFactors": [],
              "riskLevel": "normal",
              "status": "active",
              "birthPlan": null,
              "registeredByUserId": "{{worker.UserId}}",
              "gestationalAgeDays": {{today.DayNumber - lmp.DayNumber}},
              "nextContact": {{contacts[0]!.ToJsonString()}},
              "version": 1,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt");
        JsonAssert.IsIsoTimestamp(pregnancy["updatedAt"]);

        // A.6 #1 + A.8.15: weeks 12…40, dueAt = lmp + week × 7, deterministic uuid v5 ids.
        contacts.Select(c => c!["contactNo"]!.GetValue<int>()).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        contacts.Select(c => c!["weekTarget"]!.GetValue<int>()).Should().Equal(12, 20, 26, 30, 34, 36, 38, 40);
        contacts[0]!["dueAt"]!.GetValue<string>().Should().Be("2026-05-15");
        contacts[4]!["dueAt"]!.GetValue<string>().Should().Be("2026-10-16");
        foreach (var contact in contacts)
        {
            JsonAssert.HasExactKeys(contact, ContactKeys);
            var no = contact!["contactNo"]!.GetValue<int>();
            contact["id"]!.GetValue<string>().Should().Be(Ids.AncContactId(id, no));
            JsonAssert.DeepEqual(contact, Json.Obj($$"""
                {
                  "id": "{{Ids.AncContactId(id, no)}}", "pregnancyId": "{{id}}", "contactNo": {{no}},
                  "weekTarget": {{contact["weekTarget"]}}, "dueAt": "{{contact["dueAt"]}}", "doneAt": null,
                  "providerUserId": null, "findings": null, "dangerSigns": [], "triageLevel": null,
                  "triageReasons": [], "referral": null, "version": 1, "updatedAt": "<server>", "deleted": false
                }
                """), "updatedAt");
        }
    }

    [Fact]
    public async Task Register_with_edd_only_derives_the_lmp_for_scheduling_A6_case_2()
    {
        var family = await _s.NewFamily();
        var body = Body();
        body["lmp"] = null;
        body["edd"] = "2026-11-27";
        var data = (await Register(family, body, family.Owner)).Data();
        data["pregnancy"]!["lmp"].Should().BeNull();
        data["pregnancy"]!["edd"]!.GetValue<string>().Should().Be("2026-11-27");
        data["ancContacts"]![0]!["dueAt"]!.GetValue<string>().Should().Be("2026-05-15");
        data["ancContacts"]![4]!["dueAt"]!.GetValue<string>().Should().Be("2026-10-16");
    }

    [Fact]
    public async Task Register_is_idempotent_on_the_client_id()
    {
        var family = await _s.NewFamily();
        var body = Body();
        var first = (await Register(family, body, family.Owner)).Data();
        body["gravida"] = 3;
        var second = (await Register(family, body, family.Owner)).Data();
        JsonAssert.DeepEqual(second, first);

        var other = await _s.NewFamily();
        (await Register(other, body, other.Owner)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey("id").Should().BeTrue();
    }

    [Fact]
    public async Task Register_for_a_male_patient_is_a_rule_violation_A6_case_12()
    {
        var family = await _s.NewFamily(sex: "male", name: "Ram Tharu");
        (await Register(family, Body(), family.Owner)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
    }

    [Fact]
    public async Task A_second_active_pregnancy_is_a_rule_violation()
    {
        var family = await _s.NewFamily();
        (await Register(family, Body(), family.Owner)).Data();
        (await Register(family, Body(), family.Owner)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
    }

    [Fact]
    public async Task Risk_factors_set_the_risk_level_and_the_birth_plan_round_trips()
    {
        var family = await _s.NewFamily();
        var body = Body();
        body["gravida"] = 2;
        body["para"] = 1;
        body["riskFactors"] = new JsonArray("PREV_CS");
        body["birthPlan"] = Json.Obj("""
            { "facilityId": "f_0002", "transport": "Neighbour's jeep", "moneySaved": true,
              "bloodDonorName": "Hari", "bloodDonorPhone": "+9779801000011", "companionName": "Mother-in-law" }
            """);
        var pregnancy = (await Register(family, body, family.Owner)).Data()["pregnancy"]!;
        pregnancy["riskLevel"]!.GetValue<string>().Should().Be("high");
        JsonAssert.DeepEqual(pregnancy["birthPlan"], Json.Obj("""
            { "facilityId": "f_0002", "facilityName": "Rapti Provincial Hospital", "transport": "Neighbour's jeep",
              "moneySaved": true, "bloodDonorName": "Hari", "bloodDonorPhone": "+9779801000011", "companionName": "Mother-in-law" }
            """), "facilityName");
        pregnancy["birthPlan"]!["facilityName"]!.GetValue<string>().Should().NotBeNullOrEmpty("filled from the facility directory");
    }

    [Theory]
    [InlineData("lmp", "null", "lmp")]
    [InlineData("lmp", "\"2030-01-01\"", "lmp")]
    [InlineData("riskFactors", "[\"NOT_A_FACTOR\"]", "riskFactors")]
    [InlineData("gravida", "0", "gravida")]
    [InlineData("para", "5", "para")]
    [InlineData("birthPlan", "{ \"bloodDonorPhone\": \"98\" }", "birthPlan.bloodDonorPhone")]
    [InlineData("birthPlan", "{ \"facilityId\": \"f_nope\" }", "birthPlan.facilityId")]
    [InlineData("id", "\"has space\"", "id")]
    public async Task Body_is_validated(string field, string json, string detailKey)
    {
        var family = await _s.NewFamily();
        var body = Body();
        body[field] = JsonNode.Parse(json);
        if (field == "lmp") body["edd"] = null;
        (await Register(family, body, family.Owner)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey(detailKey).Should().BeTrue();
    }

    [Fact]
    public async Task Get_returns_the_bundle_and_list_returns_items()
    {
        var family = await _s.NewFamily();
        var created = (await Register(family, Body(), family.Owner)).Data();
        var id = created["pregnancy"]!["id"]!.GetValue<string>();

        var data = (await _api.Get($"/pregnancies/{id}", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(data, "pregnancy", "ancContacts", "delivery", "reminders");
        JsonAssert.DeepEqual(data["pregnancy"], created["pregnancy"]);
        JsonAssert.DeepEqual(data["ancContacts"], created["ancContacts"]);
        data["delivery"].Should().BeNull();
        // Week 28: contacts 4–8 are still ahead, each with anc_due + two anc_missed to the owner's phone.
        var reminders = data["reminders"]!.AsArray();
        reminders.Should().HaveCount(15);
        foreach (var reminder in reminders)
        {
            JsonAssert.HasExactKeys(reminder, RemindersTests.ReminderKeys);
            reminder!["pregnancyId"]!.GetValue<string>().Should().Be(id);
        }

        var list = (await _api.Get($"/patients/{family.PatientId}/pregnancies", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(list, "items");
        JsonAssert.DeepEqual(list["items"]![0], created["pregnancy"]);

        (await _api.Get($"/pregnancies/pg_{Guid.NewGuid()}", family.Owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Patch_applies_with_the_current_version_and_a_stale_version_is_a_409_with_the_current_row()
    {
        var family = await _s.NewFamily();
        var pregnancy = (await Register(family, Body(), family.Owner)).Data()["pregnancy"]!;
        var id = pregnancy["id"]!.GetValue<string>();

        var v2 = (await _api.Patch($"/pregnancies/{id}", Json.Obj("""
            { "version": 1, "riskFactors": ["AGE_GT_35"], "birthPlan": { "facilityName": "Ghorahi Health Post", "moneySaved": false } }
            """), family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(v2, "pregnancy");
        var updated = v2["pregnancy"]!;
        updated["version"]!.GetValue<int>().Should().Be(2);
        updated["riskLevel"]!.GetValue<string>().Should().Be("high");
        JsonAssert.DeepEqual(updated["birthPlan"], Json.Obj("""{ "facilityName": "Ghorahi Health Post", "moneySaved": false }"""));

        var details = (await _api.Patch($"/pregnancies/{id}", new { version = 1, status = "ended" }, family.Owner.AccessToken))
            .Details(HttpStatusCode.Conflict, "VERSION_CONFLICT");
        JsonAssert.HasExactKeys(details, "current");
        JsonAssert.HasExactKeys(details["current"], PregnancyKeys);
        JsonAssert.DeepEqual(details["current"], updated);

        (await _api.Patch($"/pregnancies/{id}", new { riskFactors = new string[0] }, family.Owner.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey("version").Should().BeTrue();
        (await _api.Patch($"/pregnancies/{id}", new { version = 2, status = "delivered" }, family.Owner.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey("status").Should().BeTrue();
    }

    [Fact]
    public async Task Patch_status_ended_closes_the_pregnancy_and_it_can_no_longer_be_changed()
    {
        var family = await _s.NewFamily();
        var id = (await Register(family, Body(), family.Owner)).Data()["pregnancy"]!["id"]!.GetValue<string>();
        var ended = (await _api.Patch($"/pregnancies/{id}", new { version = 1, status = "ended" }, family.Owner.AccessToken)).Data()["pregnancy"]!;
        ended["status"]!.GetValue<string>().Should().Be("ended");
        (await _api.Patch($"/pregnancies/{id}", new { version = 2, riskFactors = new[] { "PREV_CS" } }, family.Owner.AccessToken))
            .Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");

        // The patient can register the next pregnancy.
        (await Register(family, Body(), family.Owner)).Data();
    }
}
