using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;
using MeroSwasthya.Shared.Ids;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 PUT /pregnancies/:id/contacts/:contactNo — server-side triage (A.5) and nearestReferral.</summary>
[Collection(ApiCollection.Name)]
public sealed class AncContactsTests
{
    public static readonly string[] FacilityKeys =
        ["id", "name", "type", "hasBirthingCentre", "phone", "lat", "lng", "municipality", "distanceKm"];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public AncContactsTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    /// <summary>A family with a pregnancy (LMP 2026-02-20 → contact 4 due 2026-09-18) and a provider holding an append grant.</summary>
    private async Task<(Family Family, Session Worker, string PregnancyId)> Arrange(string invite = "HA-GHORAHI-01")
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family, invite);
        var id = $"pg_{Guid.NewGuid()}";
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies",
            PregnanciesTests.Body(id, new DateOnly(2026, 2, 20)), worker.AccessToken)).Data();
        return (family, worker, id);
    }

    /// <summary>The A.4 example body: BP 150/95 + severe headache → red.</summary>
    public static JsonObject ExampleBody() => Json.Obj("""
        {
          "doneAt": "2026-09-18T05:00:00.000Z",
          "findings": {
            "weightKg": 58, "bpSys": 150, "bpDia": 95, "fundalHeightCm": 29, "fhrBpm": 142, "hbGdl": 9.2,
            "urineProtein": "trace", "tdDoseGiven": false, "ifaGiven": true, "dewormingGiven": true,
            "calciumGiven": true, "fetalMovement": "normal"
          },
          "dangerSigns": ["SEVERE_HEADACHE_BLURRED_VISION"],
          "referral": {
            "facilityId": "f_0002", "facilityName": "Rapti Provincial Hospital",
            "reason": "Suspected pre-eclampsia", "urgency": "urgent"
          }
        }
        """);

    private Task<ApiResponse> Record(string pregnancyId, object contactNo, JsonObject body, Session who) =>
        _api.Send(HttpMethod.Put, $"/pregnancies/{pregnancyId}/contacts/{contactNo}", body, who.AccessToken);

    [Fact]
    public async Task The_part_A_example_is_red_with_the_pre_eclampsia_reason_and_a_nearest_referral()
    {
        var (family, worker, pregnancyId) = await Arrange();

        var data = (await Record(pregnancyId, 4, ExampleBody(), worker)).Data();
        JsonAssert.HasExactKeys(data, "ancContact", "nearestReferral");
        var contact = data["ancContact"]!;
        JsonAssert.HasExactKeys(contact, PregnanciesTests.ContactKeys);
        JsonAssert.DeepEqual(contact, Json.Obj($$"""
            {
              "id": "{{Ids.AncContactId(pregnancyId, 4)}}",
              "pregnancyId": "{{pregnancyId}}",
              "contactNo": 4,
              "weekTarget": 30,
              "dueAt": "2026-09-18",
              "doneAt": "2026-09-18T05:00:00.000Z",
              "providerUserId": "{{worker.UserId}}",
              "findings": {
                "weightKg": 58, "bpSys": 150, "bpDia": 95, "fundalHeightCm": 29, "fhrBpm": 142, "hbGdl": 9.2,
                "urineProtein": "trace", "tdDoseGiven": false, "ifaGiven": true, "dewormingGiven": true,
                "calciumGiven": true, "fetalMovement": "normal"
              },
              "dangerSigns": ["SEVERE_HEADACHE_BLURRED_VISION"],
              "triageLevel": "red",
              "triageReasons": [
                "Severe headache with blurred vision",
                "BP ≥ 140/90 with proteinuria or severe headache — possible pre-eclampsia"
              ],
              "referral": {
                "facilityId": "f_0002", "facilityName": "Rapti Provincial Hospital",
                "reason": "Suspected pre-eclampsia", "urgency": "urgent"
              },
              "version": 2,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt");

        // The provider works at f_0001 (Ghorahi Health Post): the nearest *other* facility with a birthing centre.
        var nearest = data["nearestReferral"]!;
        JsonAssert.HasExactKeys(nearest, FacilityKeys);
        nearest["id"]!.GetValue<string>().Should().NotBe("f_0001");
        nearest["hasBirthingCentre"]!.GetValue<bool>().Should().BeTrue();
        nearest["distanceKm"]!.GetValue<double>().Should().BeGreaterThan(0);

        // nextContact is the earliest contact with doneAt = null (A.2): 1–3 were never recorded, so still 1.
        var bundle = (await _api.Get($"/pregnancies/{pregnancyId}", family.Owner.AccessToken)).Data();
        bundle["pregnancy"]!["nextContact"]!["contactNo"]!.GetValue<int>().Should().Be(1);
        JsonAssert.DeepEqual(bundle["ancContacts"]![3], contact);
        var list = (await _api.Get($"/pregnancies/{pregnancyId}/contacts", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(list, "items");
        list["items"]!.AsArray().Should().HaveCount(8);
    }

    [Theory]
    [InlineData("""{ "findings": { "bpSys": 142, "bpDia": 88 } }""", "amber", "Raised BP (≥140/90)")]
    [InlineData("""{ "findings": { "hbGdl": 6.8 } }""", "red", "Severe anaemia (Hb < 7)")]
    [InlineData("""{ "findings": { "hbGdl": 9.2 } }""", "amber", "Anaemia (Hb 7–9.9)")]
    [InlineData("""{ "findings": { "fetalMovement": "absent" } }""", "red", "Absent fetal movement")]
    [InlineData("""{ "dangerSigns": ["SWELLING_FACE_HANDS"] }""", "amber", "Swelling of face and hands")]
    [InlineData("""{ "findings": { "bpSys": 118, "bpDia": 76, "ifaGiven": true } }""", "green", null)]
    public async Task Triage_is_evaluated_server_side_A6_cases_4_to_11(string json, string level, string? firstReason)
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Json.Obj(json);
        body["doneAt"] = "2026-09-18T05:00:00.000Z"; // week 30 → fetal-movement rule applies

        var data = (await Record(pregnancyId, 4, body, worker)).Data();
        var contact = data["ancContact"]!;
        contact["triageLevel"]!.GetValue<string>().Should().Be(level);
        if (firstReason is null)
        {
            contact["triageReasons"]!.AsArray().Should().BeEmpty();
            data["nearestReferral"].Should().BeNull("green needs no referral");
        }
        else
        {
            contact["triageReasons"]![0]!.GetValue<string>().Should().Be(firstReason);
            data["nearestReferral"].Should().NotBeNull();
        }
        contact["findings"].Should().Match(f => json.Contains("findings") ? f != null : f == null);
    }

    [Fact]
    public async Task Risk_level_high_makes_an_otherwise_clean_contact_amber_A6_case_10()
    {
        var family = await _s.NewFamily();
        var body = PregnanciesTests.Body();
        body["riskFactors"] = new JsonArray("PREV_CS");
        var id = (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", body, family.Owner.AccessToken))
            .Data()["pregnancy"]!["id"]!.GetValue<string>();

        var contact = (await Record(id, 1, Json.Obj("""{ "findings": { "bpSys": 110, "bpDia": 70 } }"""), family.Owner)).Data()["ancContact"]!;
        contact["triageLevel"]!.GetValue<string>().Should().Be("amber");
        contact["triageReasons"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Equal("High-risk pregnancy (risk factors present)");
    }

    [Fact]
    public async Task Absent_fetal_movement_before_20_weeks_is_not_red_A6_case_8()
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Json.Obj("""{ "doneAt": "2026-06-20T05:00:00.000Z", "findings": { "fetalMovement": "absent" } }"""); // day 120
        (await Record(pregnancyId, 2, body, worker)).Data()["ancContact"]!["triageLevel"]!.GetValue<string>().Should().Be("green");
    }

    [Fact]
    public async Task Owner_can_record_at_home_and_the_notes_text_and_omitted_keys_round_trip()
    {
        var (family, _, pregnancyId) = await Arrange();
        var body = Json.Obj("""{ "findings": { "weightKg": 58.0, "bpSys": 124, "bpDia": 80, "notesText": "पेट अलि दुखेको भन्नुभयो, आराम गर्न सल्लाह दिइयो।" }, "dangerSigns": [] }""");

        var data = (await Record(pregnancyId, 1, body, family.Owner)).Data();
        var contact = data["ancContact"]!;
        contact["providerUserId"]!.GetValue<string>().Should().Be(family.Owner.UserId);
        JsonAssert.IsIsoTimestamp(contact["doneAt"]);
        JsonAssert.DeepEqual(contact["findings"], Json.Obj("""{ "weightKg": 58.0, "bpSys": 124, "bpDia": 80, "notesText": "पेट अलि दुखेको भन्नुभयो, आराम गर्न सल्लाह दिइयो।" }"""));
        contact["referral"].Should().BeNull();
        contact["triageLevel"]!.GetValue<string>().Should().Be("green");
    }

    [Fact]
    public async Task Owner_with_no_facility_gets_the_birth_plan_facility_as_the_referral()
    {
        var family = await _s.NewFamily();
        var body = PregnanciesTests.Body();
        body["birthPlan"] = Json.Obj("""{ "facilityId": "f_0002" }""");
        var id = (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", body, family.Owner.AccessToken))
            .Data()["pregnancy"]!["id"]!.GetValue<string>();

        var data = (await Record(id, 1, Json.Obj("""{ "findings": { "bpSys": 150, "bpDia": 92 } }"""), family.Owner)).Data();
        data["ancContact"]!["triageLevel"]!.GetValue<string>().Should().Be("amber");
        data["nearestReferral"]!["id"]!.GetValue<string>().Should().Be("f_0002");
        data["nearestReferral"]!["distanceKm"].Should().BeNull("no origin to measure from");
    }

    [Fact]
    public async Task Fchv_can_record_a_contact_with_an_append_grant()
    {
        var (_, fchv, pregnancyId) = await Arrange("FCHV-W5-01");
        (await Record(pregnancyId, 1, Json.Obj("""{ "findings": { "bpSys": 120, "bpDia": 80 } }"""), fchv)).Data()["ancContact"]!["providerUserId"]!
            .GetValue<string>().Should().Be(fchv.UserId);
    }

    [Fact]
    public async Task Re_recording_a_contact_replaces_it_and_bumps_the_version()
    {
        var (_, worker, pregnancyId) = await Arrange();
        (await Record(pregnancyId, 3, Json.Obj("""{ "findings": { "bpSys": 150, "bpDia": 95 } }"""), worker)).Data();
        var second = (await Record(pregnancyId, 3, Json.Obj("""{ "findings": { "bpSys": 120, "bpDia": 80 } }"""), worker)).Data();
        second["ancContact"]!["version"]!.GetValue<int>().Should().Be(3);
        second["ancContact"]!["triageLevel"]!.GetValue<string>().Should().Be("green");
        second["nearestReferral"].Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData("x")]
    public async Task Contact_number_outside_1_to_8_is_a_rule_violation(object contactNo)
    {
        var (_, worker, pregnancyId) = await Arrange();
        (await Record(pregnancyId, contactNo, Json.Obj("""{ "findings": {} }"""), worker)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
    }

    [Fact]
    public async Task Contacts_cannot_be_recorded_on_an_ended_pregnancy()
    {
        var (family, worker, pregnancyId) = await Arrange();
        (await _api.Patch($"/pregnancies/{pregnancyId}", new { version = 1, status = "ended" }, family.Owner.AccessToken)).Data();
        (await Record(pregnancyId, 1, Json.Obj("""{ "findings": {} }"""), worker)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
    }

    [Theory]
    [InlineData("findings", "{ \"bpSys\": 900 }", "findings.bpSys")]
    [InlineData("findings", "{ \"hbGdl\": 40 }", "findings.hbGdl")]
    [InlineData("findings", "{ \"urineProtein\": \"positive\" }", "findings.urineProtein")]
    [InlineData("findings", "{ \"fetalMovement\": \"kicking\" }", "findings.fetalMovement")]
    [InlineData("dangerSigns", "[\"HEADACHE\"]", "dangerSigns")]
    [InlineData("referral", "{ \"facilityName\": \"Rapti\", \"reason\": \"x\", \"urgency\": \"now\" }", "referral.urgency")]
    [InlineData("referral", "{ \"reason\": \"x\", \"urgency\": \"urgent\" }", "referral.facilityName")]
    [InlineData("doneAt", "\"2099-01-01T00:00:00.000Z\"", "doneAt")]
    public async Task Body_is_validated(string field, string json, string detailKey)
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Json.Obj("{}");
        body[field] = JsonNode.Parse(json);
        (await Record(pregnancyId, 1, body, worker)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey(detailKey).Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_grant_recording_is_forbidden_and_an_unknown_pregnancy_is_not_found()
    {
        var (_, _, pregnancyId) = await Arrange();
        var stranger = await TestUsers.NewHealthWorker(_api);
        (await Record(pregnancyId, 1, Json.Obj("""{ "findings": {} }"""), stranger)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await Record($"pg_{Guid.NewGuid()}", 1, Json.Obj("""{ "findings": {} }"""), stranger)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
