using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 POST /pregnancies/:id/delivery, plus the maternal timeline / summary / redeem-bundle contributions.</summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveriesTests
{
    public static readonly string[] DeliveryKeys =
    [
        "id", "pregnancyId", "deliveredAt", "place", "mode", "outcome", "babyWeightKg", "babySex", "complications",
        "version", "updatedAt", "deleted",
    ];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public DeliveriesTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    private async Task<(Family Family, Session Worker, string PregnancyId)> Arrange()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var id = $"pg_{Guid.NewGuid()}";
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies",
            PregnanciesTests.Body(id, new DateOnly(2026, 2, 20)), worker.AccessToken)).Data();
        return (family, worker, id);
    }

    /// <summary>The A.4 example body with a fresh id (the example's deliveredAt is in the future, so a past date is used).</summary>
    public static JsonObject Body(string? id = null) => Json.Obj($$"""
        {
          "id": "{{id ?? $"dl_{Guid.NewGuid()}"}}",
          "deliveredAt": "2026-09-25T02:00:00.000Z",
          "place": "hospital",
          "mode": "normal",
          "outcome": "live_birth",
          "babyWeightKg": 2.9,
          "babySex": "female",
          "complications": []
        }
        """);

    private Task<ApiResponse> Deliver(string pregnancyId, JsonObject body, Session who) =>
        _api.Send(HttpMethod.Post, $"/pregnancies/{pregnancyId}/delivery", body, who.AccessToken);

    [Fact]
    public async Task Delivery_matches_the_part_A_example_and_closes_the_pregnancy()
    {
        var (family, worker, pregnancyId) = await Arrange();
        (await _api.Send(HttpMethod.Put, $"/pregnancies/{pregnancyId}/contacts/4", AncContactsTests.ExampleBody(), worker.AccessToken)).Data();
        var id = $"dl_{Guid.NewGuid()}";

        var data = (await Deliver(pregnancyId, Body(id), worker)).Data();
        JsonAssert.HasExactKeys(data, "delivery", "pregnancy");
        JsonAssert.HasExactKeys(data["delivery"], DeliveryKeys);
        JsonAssert.DeepEqual(data["delivery"], Json.Obj($$"""
            {
              "id": "{{id}}",
              "pregnancyId": "{{pregnancyId}}",
              "deliveredAt": "2026-09-25T02:00:00.000Z",
              "place": "hospital",
              "mode": "normal",
              "outcome": "live_birth",
              "babyWeightKg": 2.9,
              "babySex": "female",
              "complications": [],
              "version": 1,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt");

        var pregnancy = data["pregnancy"]!;
        JsonAssert.HasExactKeys(pregnancy, PregnanciesTests.PregnancyKeys);
        pregnancy["status"]!.GetValue<string>().Should().Be("delivered");
        pregnancy["version"]!.GetValue<int>().Should().Be(2);
        pregnancy["nextContact"].Should().BeNull("the contacts that never happened are no longer pending");

        // The bundle now carries the delivery; contact 4 stays, the seven others are marked not applicable (deleted).
        var bundle = (await _api.Get($"/pregnancies/{pregnancyId}", family.Owner.AccessToken)).Data();
        JsonAssert.DeepEqual(bundle["delivery"], data["delivery"]);
        var contacts = bundle["ancContacts"]!.AsArray();
        contacts.Should().HaveCount(1);
        contacts[0]!["contactNo"]!.GetValue<int>().Should().Be(4);
        contacts[0]!["deleted"]!.GetValue<bool>().Should().BeFalse();

        // …and the patient can register the next pregnancy.
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(), family.Owner.AccessToken)).Data();
    }

    [Fact]
    public async Task Delivery_is_idempotent_on_the_client_id_and_a_second_delivery_is_a_rule_violation()
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Body();
        var first = (await Deliver(pregnancyId, body, worker)).Data();
        body["babyWeightKg"] = 3.4;
        var second = (await Deliver(pregnancyId, body, worker)).Data();
        JsonAssert.DeepEqual(second, first);

        (await Deliver(pregnancyId, Body(), worker)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
    }

    [Fact]
    public async Task Delivery_on_an_ended_pregnancy_is_a_rule_violation_and_needs_append_access()
    {
        var (family, worker, pregnancyId) = await Arrange();
        var stranger = await TestUsers.NewHealthWorker(_api);
        (await Deliver(pregnancyId, Body(), stranger)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");

        (await _api.Patch($"/pregnancies/{pregnancyId}", new { version = 1, status = "ended" }, family.Owner.AccessToken)).Data();
        (await Deliver(pregnancyId, Body(), worker)).Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
        (await Deliver($"pg_{Guid.NewGuid()}", Body(), worker)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("place", "\"clinic\"", "place")]
    [InlineData("mode", "\"forceps\"", "mode")]
    [InlineData("outcome", "\"twins\"", "outcome")]
    [InlineData("babySex", "\"other\"", "babySex")]
    [InlineData("babyWeightKg", "12", "babyWeightKg")]
    [InlineData("deliveredAt", "null", "deliveredAt")]
    [InlineData("complications", "[\"\"]", "complications")]
    public async Task Body_is_validated(string field, string json, string detailKey)
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Body();
        body[field] = JsonNode.Parse(json);
        (await Deliver(pregnancyId, body, worker)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey(detailKey).Should().BeTrue();
    }

    [Fact]
    public async Task Stillbirth_with_complications_and_no_baby_details_round_trips()
    {
        var (_, worker, pregnancyId) = await Arrange();
        var body = Json.Obj($$"""
            { "id": "dl_{{Guid.NewGuid()}}", "deliveredAt": "2026-09-20T22:10:00.000Z", "place": "on_the_way", "mode": "assisted",
              "outcome": "stillbirth", "babyWeightKg": null, "babySex": null, "complications": ["PPH", " retained placenta "] }
            """);
        var delivery = (await Deliver(pregnancyId, body, worker)).Data()["delivery"]!;
        delivery["babyWeightKg"].Should().BeNull();
        delivery["babySex"].Should().BeNull();
        delivery["complications"]!.AsArray().Select(c => c!.GetValue<string>()).Should().Equal("PPH", "retained placenta");
    }

    // ---- Timeline, summary and redeem-bundle contributions

    [Fact]
    public async Task Timeline_carries_pregnancy_registered_recorded_contacts_with_badge_and_delivery()
    {
        var (family, worker, pregnancyId) = await Arrange();
        var contact = (await _api.Send(HttpMethod.Put, $"/pregnancies/{pregnancyId}/contacts/4", AncContactsTests.ExampleBody(), worker.AccessToken))
            .Data()["ancContact"]!;
        var delivery = (await Deliver(pregnancyId, Body(), worker)).Data()["delivery"]!;

        // Registered today (createdAt), delivery dated 2026-09-25, contact dated 2026-09-18: newest first.
        var items = (await _api.Get($"/patients/{family.PatientId}/timeline", family.Owner.AccessToken)).Data()["items"]!.AsArray();
        items.Select(i => i!["kind"]!.GetValue<string>()).Should().Equal("pregnancy_registered", "delivery", "anc_contact")
            .And.Subject.Should().HaveCount(3, "only the recorded contact is an event; the seven pending ones are not");

        var ancItem = items[2]!;
        JsonAssert.HasExactKeys(ancItem, "kind", "at", "title", "subtitle", "badge", "refId", "payload");
        JsonAssert.DeepEqual(ancItem, Json.Obj($$"""
            {
              "kind": "anc_contact",
              "at": "2026-09-18T05:00:00.000Z",
              "title": "ANC contact 4 (week 30)",
              "subtitle": "BP 150/95 · Hb 9.2 · referred",
              "badge": "red",
              "refId": "{{contact["id"]}}",
              "payload": {{contact.ToJsonString()}}
            }
            """));

        var deliveryItem = items[1]!;
        deliveryItem["at"]!.GetValue<string>().Should().Be("2026-09-25T02:00:00.000Z");
        deliveryItem["title"]!.GetValue<string>().Should().Be("Delivery — live birth");
        deliveryItem["subtitle"]!.GetValue<string>().Should().Be("Hospital · Normal delivery · 2.9 kg · girl");
        deliveryItem["badge"].Should().BeNull();
        JsonAssert.DeepEqual(deliveryItem["payload"], delivery);

        var registered = items[0]!;
        registered["title"]!.GetValue<string>().Should().Be("Pregnancy registered");
        registered["subtitle"]!.GetValue<string>().Should().Be("EDD 2026-11-27 · G1 P0 · normal risk");
        registered["refId"]!.GetValue<string>().Should().Be(pregnancyId);
        JsonAssert.HasExactKeys(registered["payload"], PregnanciesTests.PregnancyKeys);
        registered["payload"]!["status"]!.GetValue<string>().Should().Be("delivered");

        // Paging: `before` the delivery leaves only the contact.
        var older = (await _api.Get($"/patients/{family.PatientId}/timeline?before=2026-09-25T02:00:00.000Z", family.Owner.AccessToken))
            .Data()["items"]!.AsArray();
        older.Select(i => i!["kind"]!.GetValue<string>()).Should().Equal("anc_contact");
    }

    [Fact]
    public async Task Summary_and_redeem_bundle_carry_the_active_pregnancy_until_it_is_delivered()
    {
        var (family, _, pregnancyId) = await Arrange();
        var expected = (await _api.Get($"/pregnancies/{pregnancyId}", family.Owner.AccessToken)).Data();

        var summary = (await _api.Get($"/patients/{family.PatientId}", family.Owner.AccessToken)).Data()["summary"]!;
        JsonAssert.DeepEqual(summary["activePregnancy"], expected["pregnancy"]);

        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);
        var bundle = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        JsonAssert.DeepEqual(bundle["pregnancy"], expected["pregnancy"]);
        JsonAssert.DeepEqual(bundle["ancContacts"], expected["ancContacts"]);
        bundle["timeline"]!.AsArray().Select(i => i!["kind"]!.GetValue<string>()).Should().Contain("pregnancy_registered");

        (await Deliver(pregnancyId, Body(), worker)).Data();
        (await _api.Get($"/patients/{family.PatientId}", family.Owner.AccessToken)).Data()["summary"]!["activePregnancy"].Should().BeNull();
        var after = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        after["pregnancy"].Should().BeNull();
        after["ancContacts"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task A_grant_limited_to_other_sections_withholds_the_pregnancy()
    {
        var (family, _, _) = await Arrange();
        var share = await _s.Share(family, sections: ["summary", "visits"]);
        var worker = await TestUsers.NewHealthWorker(_api);
        var bundle = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        bundle["pregnancy"].Should().BeNull();
        bundle["ancContacts"]!.AsArray().Should().BeEmpty();
        bundle["timeline"]!.AsArray().Select(i => i!["kind"]!.GetValue<string>()).Should().NotContain("pregnancy_registered");
    }
}
