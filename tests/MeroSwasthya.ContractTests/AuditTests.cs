using System.Net;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.2 AuditEntry, A.4 GET /patients/:id/audit.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuditTests
{
    private static readonly string[] AuditKeys = ["id", "patientId", "actorUserId", "actorName", "actorFacilityName", "action", "at"];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public AuditTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    [Fact]
    public async Task Every_action_is_logged_newest_first_in_the_part_A_shape()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Data();
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", Scenario.VisitBody(), worker.AccessToken)).Data();
        await _s.UploadDocument(worker, family.PatientId, Scenario.Jpeg());
        (await _api.Post($"/grants/{share["grant"]!["id"]}/revoke", null, family.Owner.AccessToken)).Data();

        var data = (await _api.Get($"/patients/{family.PatientId}/audit", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(data, "items");
        var items = data["items"]!.AsArray();
        items.Select(i => i!["action"]!.GetValue<string>()).Should().Equal(
            "grant_revoked", "document_added", "visit_added", "record_viewed", "grant_redeemed", "grant_created");

        foreach (var item in items)
        {
            JsonAssert.HasExactKeys(item, AuditKeys);
            item!["id"]!.GetValue<string>().Should().StartWith("au_");
            JsonAssert.IsIsoTimestamp(item["at"]);
        }

        var redeemed = items.Single(i => i!["action"]!.GetValue<string>() == "grant_redeemed")!;
        JsonAssert.DeepEqual(redeemed, Json.Obj($$"""
            {
              "id": "<id>", "patientId": "{{family.PatientId}}", "actorUserId": "{{worker.UserId}}",
              "actorName": "Test Health Worker", "actorFacilityName": "Ghorahi Health Post",
              "action": "grant_redeemed", "at": "<ts>"
            }
            """), "id", "at");
        items.Last()!["actorFacilityName"].Should().BeNull("the patient who created the grant has no facility");
    }

    [Fact]
    public async Task Record_viewed_is_written_at_most_once_per_10_minutes_per_provider()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        for (var i = 0; i < 3; i++)
            (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Data();

        // The owner reading their own record is not an access event.
        (await _api.Get($"/patients/{family.PatientId}", family.Owner.AccessToken)).Data();

        (await _s.AuditActions(family)).Count(a => a == "record_viewed").Should().Be(1);
    }

    [Fact]
    public async Task Audit_is_owner_only()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        (await _api.Get($"/patients/{family.PatientId}/audit", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/patients/{family.PatientId}/audit")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }
}
