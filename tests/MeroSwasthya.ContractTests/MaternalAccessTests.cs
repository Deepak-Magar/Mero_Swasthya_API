using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>Maternal endpoints follow the Clinical access rules (owner or active grant; A.3, A.6 #16) and write the audit log.</summary>
[Collection(ApiCollection.Name)]
public sealed class MaternalAccessTests
{
    private readonly ApiClient _api;
    private readonly Scenario _s;

    public MaternalAccessTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    private static JsonObject Contact() => Json.Obj("""{ "findings": { "bpSys": 120, "bpDia": 80 } }""");

    private async Task<string> RegisterAsOwner(Family family)
    {
        var id = $"pg_{Guid.NewGuid()}";
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(id), family.Owner.AccessToken)).Data();
        return id;
    }

    [Fact]
    public async Task Every_maternal_write_is_audited_as_contact_recorded_with_the_actor()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var id = $"pg_{Guid.NewGuid()}";
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(id), worker.AccessToken)).Data();
        (await _api.Send(HttpMethod.Put, $"/pregnancies/{id}/contacts/1", Contact(), worker.AccessToken)).Data();
        (await _api.Patch($"/pregnancies/{id}", new { version = 1, riskFactors = new[] { "AGE_GT_35" } }, worker.AccessToken)).Data();
        (await _api.Send(HttpMethod.Post, $"/pregnancies/{id}/delivery", DeliveriesTests.Body(), worker.AccessToken)).Data();

        var items = (await _api.Get($"/patients/{family.PatientId}/audit", family.Owner.AccessToken)).Data()["items"]!.AsArray();
        items.Select(i => i!["action"]!.GetValue<string>()).Should().Equal(
            "contact_recorded", "contact_recorded", "contact_recorded", "contact_recorded", "grant_redeemed", "grant_created");
        foreach (var item in items.Take(4))
        {
            item!["actorUserId"]!.GetValue<string>().Should().Be(worker.UserId);
            item["actorName"]!.GetValue<string>().Should().Be("Test Health Worker");
            item["actorFacilityName"]!.GetValue<string>().Should().Be("Ghorahi Health Post");
        }
    }

    [Fact]
    public async Task A_failed_write_leaves_no_audit_line_and_an_idempotent_replay_adds_none()
    {
        var family = await _s.NewFamily(sex: "male", name: "Ram Tharu");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(), family.Owner.AccessToken))
            .Error(HttpStatusCode.UnprocessableEntity, "RULE_VIOLATION");
        (await _s.AuditActions(family)).Should().BeEmpty();

        var woman = await _s.NewFamily();
        var body = PregnanciesTests.Body();
        (await _api.Send(HttpMethod.Post, $"/patients/{woman.PatientId}/pregnancies", body, woman.Owner.AccessToken)).Data();
        (await _api.Send(HttpMethod.Post, $"/patients/{woman.PatientId}/pregnancies", body, woman.Owner.AccessToken)).Data();
        (await _s.AuditActions(woman)).Count(a => a == "contact_recorded").Should().Be(1);
    }

    [Fact]
    public async Task A_health_worker_reading_the_pregnancy_is_logged_as_record_viewed_once_per_10_minutes()
    {
        var family = await _s.NewFamily();
        var id = await RegisterAsOwner(family);
        var worker = await _s.WorkerWithGrant(family);

        (await _api.Get($"/pregnancies/{id}", worker.AccessToken)).Data();
        (await _api.Get($"/pregnancies/{id}/contacts", worker.AccessToken)).Data();
        (await _api.Get($"/patients/{family.PatientId}/pregnancies", worker.AccessToken)).Data();
        (await _api.Get($"/pregnancies/{id}", family.Owner.AccessToken)).Data(); // the owner is not an access event

        (await _s.AuditActions(family)).Count(a => a == "record_viewed").Should().Be(1);
    }

    [Fact]
    public async Task A_read_grant_allows_reading_but_not_writing()
    {
        var family = await _s.NewFamily();
        var id = await RegisterAsOwner(family);
        var reader = await _s.WorkerWithGrant(family, scope: "read");

        (await _api.Get($"/pregnancies/{id}", reader.AccessToken)).Data();
        (await _api.Send(HttpMethod.Put, $"/pregnancies/{id}/contacts/1", Contact(), reader.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Patch($"/pregnancies/{id}", new { version = 1, status = "ended" }, reader.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Send(HttpMethod.Post, $"/pregnancies/{id}/delivery", DeliveriesTests.Body(), reader.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(), reader.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task After_the_24h_window_every_maternal_call_is_GRANT_EXPIRED_and_a_revoked_grant_is_FORBIDDEN()
    {
        var family = await _s.NewFamily();
        var id = await RegisterAsOwner(family);
        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        var grantId = share["grant"]!["id"]!.GetValue<string>();

        // A.6 #16: 25 h later.
        await _s.SetGrantColumn(grantId, "access_until", "now() - interval '1 hour'");
        (await _api.Get($"/pregnancies/{id}", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
        (await _api.Send(HttpMethod.Put, $"/pregnancies/{id}/contacts/1", Contact(), worker.AccessToken)).Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
        (await _api.Send(HttpMethod.Post, $"/pregnancies/{id}/delivery", DeliveriesTests.Body(), worker.AccessToken)).Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");

        await _s.SetGrantColumn(grantId, "access_until", "now() + interval '1 hour'");
        (await _api.Get($"/pregnancies/{id}", worker.AccessToken)).Data();
        (await _api.Post($"/grants/{grantId}/revoke", null, family.Owner.AccessToken)).Data();
        (await _api.Get($"/pregnancies/{id}", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Strangers_and_anonymous_callers_are_kept_out()
    {
        var family = await _s.NewFamily();
        var id = await RegisterAsOwner(family);
        var other = await TestUsers.NewAccount(_api);
        var worker = await TestUsers.NewHealthWorker(_api, "FCHV-W5-01");

        (await _api.Get($"/pregnancies/{id}", other.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/patients/{family.PatientId}/pregnancies", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(), worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/pregnancies/{id}")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Send(HttpMethod.Put, $"/pregnancies/{id}/contacts/1", Contact())).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Get($"/pregnancies/{id}", "not-a-token")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }
}
