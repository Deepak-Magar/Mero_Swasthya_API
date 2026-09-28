using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 "Access grants (QR)", A.7, A.6 #15/#16, addendum §4.</summary>
[Collection(ApiCollection.Name)]
public sealed class GrantsTests
{
    public static readonly string[] GrantKeys =
        ["id", "patientId", "scope", "expiresAt", "redeemedByUserId", "redeemedAt", "revokedAt", "accessUntil", "longLived", "sections"];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public GrantsTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    private static JsonObject JwtPayload(string token)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)))!.AsObject();
    }

    // ---- POST /grants

    [Fact]
    public async Task Create_returns_grant_token_and_SWC1_payload()
    {
        var family = await _s.NewFamily();
        var data = await _s.Share(family, scope: "append", ttlMinutes: 10);

        JsonAssert.HasExactKeys(data, "grant", "token", "qrPayload");
        var token = data["token"]!.GetValue<string>();
        data["qrPayload"]!.GetValue<string>().Should().Be("SWC1:" + token);

        var grant = data["grant"]!.AsObject();
        JsonAssert.HasExactKeys(grant, [.. GrantKeys, "token"]); // token only on creation (A.2)
        grant["token"]!.GetValue<string>().Should().Be(token);
        grant["id"]!.GetValue<string>().Should().StartWith("g_");
        JsonAssert.DeepEqual(grant, Json.Obj($$"""
            {
              "id": "<id>", "patientId": "{{family.PatientId}}", "scope": "append", "token": "<token>",
              "expiresAt": "<ts>", "redeemedByUserId": null, "redeemedAt": null, "revokedAt": null,
              "accessUntil": null, "longLived": false, "sections": []
            }
            """), "id", "token", "expiresAt");

        // A.7 claims: { typ:"grant", gid, pid, scope, iat, exp }, exp = iat + ttlMinutes·60.
        var claims = JwtPayload(token);
        claims["typ"]!.GetValue<string>().Should().Be("grant");
        claims["gid"]!.GetValue<string>().Should().Be(grant["id"]!.GetValue<string>());
        claims["pid"]!.GetValue<string>().Should().Be(family.PatientId);
        claims["scope"]!.GetValue<string>().Should().Be("append");
        (claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>()).Should().Be(600);
        var expiresAt = DateTime.Parse(grant["expiresAt"]!.GetValue<string>()).ToUniversalTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Create_defaults_to_append_for_10_minutes()
    {
        var family = await _s.NewFamily();
        var data = await _s.Share(family, scope: null);
        data["grant"]!["scope"]!.GetValue<string>().Should().Be("append");
        var claims = JwtPayload(data["token"]!.GetValue<string>());
        (claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>()).Should().Be(600);
    }

    [Fact]
    public async Task Create_is_owner_only()
    {
        var family = await _s.NewFamily();
        var stranger = await TestUsers.NewAccount(_api);
        (await _api.Post("/grants", new { patientId = family.PatientId, scope = "read" }, stranger.AccessToken))
            .Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Post("/grants", new { patientId = Scenario.NewPatientId() }, family.Owner.AccessToken))
            .Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("""{ "scope": "read" }""", "patientId")]
    [InlineData("""{ "patientId": "p_x", "scope": "write" }""", "scope")]
    [InlineData("""{ "patientId": "p_x", "ttlMinutes": 0 }""", "ttlMinutes")]
    [InlineData("""{ "patientId": "p_x", "ttlMinutes": 525601 }""", "ttlMinutes")]
    [InlineData("""{ "patientId": "p_x", "ttlMinutes": 525600, "scope": "append" }""", "scope")]
    [InlineData("""{ "patientId": "p_x", "sections": ["summary", "everything"] }""", "sections")]
    public async Task Create_validates(string body, string field)
    {
        var owner = await TestUsers.NewAccount(_api);
        (await _api.PostRaw("/grants", body, owner.AccessToken)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR")
            .ContainsKey(field).Should().BeTrue();
    }

    [Fact]
    public async Task Create_is_rate_limited_to_20_per_patient_per_hour()
    {
        var family = await _s.NewFamily();
        for (var i = 0; i < 20; i++) await _s.Share(family);
        (await _api.Post("/grants", new { patientId = family.PatientId }, family.Owner.AccessToken))
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    // ---- POST /grants/redeem

    [Fact]
    public async Task Redeem_returns_the_bundle_and_opens_a_24h_window()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);

        var data = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();

        JsonAssert.HasExactKeys(data, "grant", "patient", "summary", "timeline", "pregnancy", "ancContacts");
        var grant = data["grant"]!;
        JsonAssert.HasExactKeys(grant, GrantKeys); // no token after creation
        grant["redeemedByUserId"]!.GetValue<string>().Should().Be(worker.UserId);
        var redeemedAt = DateTime.Parse(grant["redeemedAt"]!.GetValue<string>()).ToUniversalTime();
        DateTime.Parse(grant["accessUntil"]!.GetValue<string>()).ToUniversalTime().Should().Be(redeemedAt.AddHours(24));

        JsonAssert.HasExactKeys(data["patient"], PatientsTests.PatientKeys);
        data["patient"]!["id"]!.GetValue<string>().Should().Be(family.PatientId);
        data["patient"]!["allergies"]!.AsArray().Select(a => a!.GetValue<string>()).Should().Equal("sulpha");
        JsonAssert.HasExactKeys(data["summary"], "activeProblems", "currentMedicines", "allergies", "lastVitals",
            "activePregnancy", "lastVisitAt", "visitCount");
        data["timeline"].Should().BeOfType<JsonArray>();
        data["pregnancy"].Should().BeNull("Maternal module not yet implemented: hook returns nothing");
        data["ancContacts"]!.AsArray().Should().BeEmpty();

        (await _s.AuditActions(family)).Should().Equal("grant_redeemed", "grant_created");
    }

    [Fact]
    public async Task Redeem_bundle_for_Ram_carries_his_visits_and_documents()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var share = (await _api.Post("/grants", new { patientId = "p_a1a1a1a1-0000-4000-8000-000000000002", scope = "read" },
            owner.AccessToken)).Data();
        var worker = await TestUsers.NewHealthWorker(_api);

        var data = (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();

        var kinds = data["timeline"]!.AsArray().Select(i => i!["kind"]!.GetValue<string>()).ToList();
        kinds.Count(k => k == "visit").Should().Be(4);
        kinds.Count(k => k == "document").Should().Be(2);
        data["summary"]!["visitCount"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task Redeem_is_for_providers_and_fchvs_only()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var patientUser = await TestUsers.NewAccount(_api);
        (await _s.Redeem(patientUser, share["qrPayload"]!.GetValue<string>())).Error(HttpStatusCode.Forbidden, "FORBIDDEN");

        var fchv = await TestUsers.NewHealthWorker(_api, "FCHV-W5-01");
        (await _s.Redeem(fchv, share["qrPayload"]!.GetValue<string>())).Data();
    }

    [Fact]
    public async Task Redeem_again_by_the_same_provider_is_idempotent()
    {
        var family = await _s.NewFamily();
        var qr = (await _s.Share(family))["qrPayload"]!.GetValue<string>();
        var worker = await TestUsers.NewHealthWorker(_api);

        var first = (await _s.Redeem(worker, qr)).Data()["grant"];
        var second = (await _s.Redeem(worker, qr)).Data()["grant"];
        JsonAssert.DeepEqual(second, first);
        (await _s.AuditActions(family)).Count(a => a == "grant_redeemed").Should().Be(1);
    }

    [Fact]
    public async Task Redeem_by_a_second_provider_is_ALREADY_REDEEMED()
    {
        var family = await _s.NewFamily();
        var qr = (await _s.Share(family))["qrPayload"]!.GetValue<string>();
        (await _s.Redeem(await TestUsers.NewHealthWorker(_api), qr)).Data();

        (await _s.Redeem(await TestUsers.NewHealthWorker(_api), qr)).Error(HttpStatusCode.Conflict, "ALREADY_REDEEMED");
    }

    [Fact]
    public async Task Redeem_after_10_minutes_is_GRANT_EXPIRED() // A.6 #15
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        await _s.SetGrantColumn(share["grant"]!["id"]!.GetValue<string>(), "expires_at", "now() - interval '1 second'");

        (await _s.Redeem(await TestUsers.NewHealthWorker(_api), share["qrPayload"]!.GetValue<string>()))
            .Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
    }

    [Theory]
    [InlineData("HELLO")]
    [InlineData("SWC2:abc.def.ghi")]
    [InlineData("SWC1:not-a-jwt")]
    public async Task Redeem_rejects_foreign_payloads(string payload)
    {
        (await _s.Redeem(await TestUsers.NewHealthWorker(_api), payload))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey("qrPayload").Should().BeTrue();
    }

    [Fact]
    public async Task Redeem_rejects_an_access_token_dressed_as_a_grant()
    {
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, "SWC1:" + worker.AccessToken)).Error(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---- POST /grants/:id/revoke

    [Fact]
    public async Task Revoke_ends_provider_access_immediately()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var grantId = share["grant"]!["id"]!.GetValue<string>();
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Data();

        var revoked = (await _api.Post($"/grants/{grantId}/revoke", null, family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(revoked, "grant");
        JsonAssert.HasExactKeys(revoked["grant"], GrantKeys);
        JsonAssert.IsIsoTimestamp(revoked["grant"]!["revokedAt"]);

        (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        var ids = (await _api.Get("/patients", worker.AccessToken)).Data()["items"]!.AsArray().Select(p => p!["id"]!.GetValue<string>());
        ids.Should().NotContain(family.PatientId);
        (await _s.AuditActions(family)).First().Should().Be("grant_revoked");
    }

    [Fact]
    public async Task Revoked_code_cannot_be_redeemed()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        (await _api.Post($"/grants/{share["grant"]!["id"]}/revoke", null, family.Owner.AccessToken)).Data();
        (await _s.Redeem(await TestUsers.NewHealthWorker(_api), share["qrPayload"]!.GetValue<string>()))
            .Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
    }

    [Fact]
    public async Task Revoke_is_owner_only()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();

        (await _api.Post($"/grants/{share["grant"]!["id"]}/revoke", null, worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Post("/grants/g_nope/revoke", null, family.Owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- Access through grants

    [Fact]
    public async Task Provider_patient_list_and_detail_include_granted_patients()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);

        var ids = (await _api.Get("/patients", worker.AccessToken)).Data()["items"]!.AsArray().Select(p => p!["id"]!.GetValue<string>());
        ids.Should().Contain(family.PatientId);
        (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Data();
        (await _api.Get($"/patients/{family.PatientId}/timeline", worker.AccessToken)).Data();
        // A grant is read/append, never ownership.
        (await _api.Patch($"/patients/{family.PatientId}", new { version = 1, name = "X" }, worker.AccessToken))
            .Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Access_25h_after_redeem_is_GRANT_EXPIRED() // A.6 #16
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family);
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _s.Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        await _s.SetGrantColumn(share["grant"]!["id"]!.GetValue<string>(), "access_until", "now() - interval '1 hour'");

        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", Scenario.VisitBody(), worker.AccessToken))
            .Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
        (await _api.Get($"/patients/{family.PatientId}", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "GRANT_EXPIRED");
        var ids = (await _api.Get("/patients", worker.AccessToken)).Data()["items"]!.AsArray().Select(p => p!["id"]!.GetValue<string>());
        ids.Should().NotContain(family.PatientId);
    }

    [Fact]
    public async Task Sections_filter_the_bundle_but_the_patient_row_always_travels()
    {
        var family = await _s.NewFamily();
        var owner = family.Owner;
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", Scenario.VisitBody(), owner.AccessToken)).Data();
        var share = await _s.Share(family, scope: "read", sections: ["pregnancy"]);
        share["grant"]!["sections"]!.AsArray().Select(s => s!.GetValue<string>()).Should().Equal("pregnancy");

        var data = (await _s.Redeem(await TestUsers.NewHealthWorker(_api), share["qrPayload"]!.GetValue<string>())).Data();

        data["patient"]!["allergies"]!.AsArray().Should().HaveCount(1);
        JsonAssert.DeepEqual(data["summary"], Json.Obj("""
            { "activeProblems": [], "currentMedicines": [], "allergies": [], "lastVitals": null,
              "activePregnancy": null, "lastVisitAt": null, "visitCount": 0 }
            """));
        data["timeline"]!.AsArray().Should().BeEmpty("visits were not shared");
        data["grant"]!["sections"]!.AsArray().Select(s => s!.GetValue<string>()).Should().Equal("pregnancy");
    }

    // ---- A.7 printed card

    [Fact]
    public async Task Long_lived_card_needs_the_owners_pin()
    {
        var family = await _s.NewFamily();
        var share = await _s.Share(family, scope: "read", ttlMinutes: 525_600);
        share["grant"]!["longLived"]!.GetValue<bool>().Should().BeTrue();
        var qr = share["qrPayload"]!.GetValue<string>();
        var worker = await TestUsers.NewHealthWorker(_api);

        (await _s.Redeem(worker, qr)).Details(HttpStatusCode.Forbidden, "FORBIDDEN")["pin"]!.GetValue<string>().Should().Be("required");
        (await _s.Redeem(worker, qr, pin: "0000")).Details(HttpStatusCode.Forbidden, "FORBIDDEN")["pin"]!.GetValue<string>().Should().Be("invalid");

        var data = (await _s.Redeem(worker, qr, pin: Scenario.OwnerPin)).Data();
        data["grant"]!["redeemedByUserId"]!.GetValue<string>().Should().Be(worker.UserId);
    }

    [Fact]
    public async Task Read_scope_does_not_allow_adding_visits()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family, scope: "read");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", Scenario.VisitBody(), worker.AccessToken))
            .Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
