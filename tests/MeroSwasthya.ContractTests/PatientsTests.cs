using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.4 "Patients (family profiles)".</summary>
[Collection(ApiCollection.Name)]
public sealed class PatientsTests(ApiFactory factory)
{
    public static readonly string[] PatientKeys =
    [
        "id", "ownerUserId", "name", "sex", "dob", "bloodGroup", "ward", "municipality", "allergies",
        "chronicConditions", "emergencyContactPhone", "version", "updatedAt", "deleted",
    ];

    private const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    private const string RamId = "p_a1a1a1a1-0000-4000-8000-000000000002";
    private const string AaravId = "p_a1a1a1a1-0000-4000-8000-000000000006";

    private readonly ApiClient _api = new(factory.CreateClient());

    private static string NewId() => $"p_{Guid.NewGuid()}";

    /// <summary>The A.4 POST /patients example body, with a fresh id.</summary>
    private static JsonObject ExampleBody(string id) => Json.Obj($$"""
        {
          "id": "{{id}}",
          "name": "Sita Chaudhary",
          "sex": "female",
          "dob": "2002-04-11",
          "bloodGroup": "B+",
          "ward": 5,
          "municipality": "Ghorahi",
          "allergies": [],
          "chronicConditions": [],
          "emergencyContactPhone": "+9779801000009"
        }
        """);

    private async Task<(Session Owner, JsonObject Patient)> CreateOwnedPatient(JsonObject? body = null)
    {
        var owner = await TestUsers.NewAccount(_api);
        var id = NewId();
        var patient = (await _api.Send(HttpMethod.Post, "/patients", body ?? ExampleBody(id), owner.AccessToken))
            .Data()["patient"]!.AsObject();
        return (owner, patient);
    }

    // ---- POST /patients

    [Fact]
    public async Task Create_returns_the_part_A_patient_with_version_1()
    {
        var owner = await TestUsers.NewAccount(_api);
        var id = NewId();
        var data = (await _api.Send(HttpMethod.Post, "/patients", ExampleBody(id), owner.AccessToken)).Data();

        JsonAssert.HasExactKeys(data, "patient");
        var patient = data["patient"]!;
        JsonAssert.HasExactKeys(patient, PatientKeys);
        JsonAssert.DeepEqual(patient, Json.Obj($$"""
            {
              "id": "{{id}}",
              "ownerUserId": "{{owner.UserId}}",
              "name": "Sita Chaudhary",
              "sex": "female",
              "dob": "2002-04-11",
              "bloodGroup": "B+",
              "ward": 5,
              "municipality": "Ghorahi",
              "allergies": [],
              "chronicConditions": [],
              "emergencyContactPhone": "+9779801000009",
              "version": 1,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt");
        JsonAssert.IsIsoTimestamp(patient["updatedAt"]);
    }

    [Fact]
    public async Task Create_is_idempotent_on_the_client_id()
    {
        var owner = await TestUsers.NewAccount(_api);
        var id = NewId();
        var first = (await _api.Send(HttpMethod.Post, "/patients", ExampleBody(id), owner.AccessToken)).Data()["patient"];

        var changed = ExampleBody(id);
        changed["name"] = "Somebody Else";
        var second = (await _api.Send(HttpMethod.Post, "/patients", changed, owner.AccessToken)).Data()["patient"];

        JsonAssert.DeepEqual(second, first); // same row, same version, same updatedAt — the retry changed nothing
        var items = (await _api.Get("/patients", owner.AccessToken)).Data()["items"]!.AsArray();
        items.Count(p => p!["id"]!.GetValue<string>() == id).Should().Be(1, "no duplicate row");
    }

    [Fact]
    public async Task Create_with_an_id_owned_by_another_account_is_forbidden()
    {
        var (_, patient) = await CreateOwnedPatient();
        var intruder = await TestUsers.NewAccount(_api);

        (await _api.Send(HttpMethod.Post, "/patients", ExampleBody(patient["id"]!.GetValue<string>()), intruder.AccessToken))
            .Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Create_applies_defaults_for_omitted_optional_fields()
    {
        var owner = await TestUsers.NewAccount(_api);
        var id = NewId();
        var patient = (await _api.Post("/patients", new { id, name = "Hari", sex = "male", dob = "2020-01-02" }, owner.AccessToken))
            .Data()["patient"]!;

        JsonAssert.HasExactKeys(patient, PatientKeys);
        patient["bloodGroup"].Should().BeNull();
        patient["ward"].Should().BeNull();
        patient["municipality"].Should().BeNull();
        patient["emergencyContactPhone"].Should().BeNull();
        patient["allergies"]!.AsArray().Should().BeEmpty();
        patient["chronicConditions"]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "name": "X", "sex": "male", "dob": "2000-01-01" }""", "id")]
    [InlineData("""{ "id": "has space", "name": "X", "sex": "male", "dob": "2000-01-01" }""", "id")]
    [InlineData("""{ "id": "p_1", "sex": "male", "dob": "2000-01-01" }""", "name")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "boy", "dob": "2000-01-01" }""", "sex")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male" }""", "dob")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "11/04/2002" }""", "dob")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2999-01-01" }""", "dob")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "bloodGroup": "C+" }""", "bloodGroup")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "ward": 0 }""", "ward")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "ward": "five" }""", "ward")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "emergencyContactPhone": "12345" }""", "emergencyContactPhone")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "allergies": [""] }""", "allergies")]
    [InlineData("""{ "id": "p_1", "name": "X", "sex": "male", "dob": "2000-01-01", "chronicConditions": ["not a code!"] }""", "chronicConditions")]
    public async Task Create_validates_every_field(string body, string field)
    {
        var owner = await TestUsers.NewAccount(_api);
        var details = (await _api.PostRaw("/patients", body, owner.AccessToken)).Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.ContainsKey(field).Should().BeTrue(details.ToJsonString());
    }

    [Fact]
    public async Task Patients_endpoints_require_an_access_token()
    {
        (await _api.Get("/patients")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Send(HttpMethod.Post, "/patients", ExampleBody(NewId()))).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Get($"/patients/{SitaId}")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    // ---- GET /patients

    [Fact]
    public async Task List_for_the_seeded_patient_returns_the_household()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var items = (await _api.Get("/patients", owner.AccessToken)).Data()["items"]!.AsArray();

        items.Select(p => p!["id"]!.GetValue<string>()).Should().Contain([SitaId, RamId, AaravId]);
        foreach (var item in items) JsonAssert.HasExactKeys(item, PatientKeys);

        var sita = items.Single(p => p!["id"]!.GetValue<string>() == SitaId)!;
        JsonAssert.DeepEqual(sita, Json.Obj($$"""
            {
              "id": "{{SitaId}}",
              "ownerUserId": "u_11111111-1111-4111-8111-111111111111",
              "name": "Sita Chaudhary",
              "sex": "female",
              "dob": "2002-04-11",
              "bloodGroup": "B+",
              "ward": 5,
              "municipality": "Ghorahi",
              "allergies": ["sulpha"],
              "chronicConditions": [],
              "emergencyContactPhone": "+9779801000009",
              "version": 1,
              "updatedAt": "<server>",
              "deleted": false
            }
            """), "updatedAt", "version");

        var aarav = items.Single(p => p!["id"]!.GetValue<string>() == AaravId)!;
        var dob = DateOnly.Parse(aarav["dob"]!.GetValue<string>());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var months = (today.Year - dob.Year) * 12 + today.Month - dob.Month;
        months.Should().Be(38, "Aarav is three years and two months old");
        aarav["sex"]!.GetValue<string>().Should().Be("male");
    }

    [Fact]
    public async Task List_only_returns_my_own_profiles_for_a_patient()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var items = (await _api.Get("/patients", owner.AccessToken)).Data()["items"]!.AsArray();

        items.Select(p => p!["id"]!.GetValue<string>()).Should().Equal(patient["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task List_for_a_health_worker_without_grants_excludes_other_households()
    {
        var worker = await TestUsers.NewHealthWorker(_api);
        var own = (await _api.Send(HttpMethod.Post, "/patients", ExampleBody(NewId()), worker.AccessToken)).Data()["patient"]!;

        var ids = (await _api.Get("/patients", worker.AccessToken)).Data()["items"]!.AsArray()
            .Select(p => p!["id"]!.GetValue<string>()).ToList();
        ids.Should().Equal(own["id"]!.GetValue<string>());
        ids.Should().NotContain(SitaId);
    }

    // ---- GET /patients/:id

    [Fact]
    public async Task Detail_for_a_new_patient_has_the_empty_summary_shape()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var data = (await _api.Get($"/patients/{patient["id"]}", owner.AccessToken)).Data();

        JsonAssert.HasExactKeys(data, "patient", "summary");
        JsonAssert.DeepEqual(data["patient"], patient);
        JsonAssert.DeepEqual(data["summary"], Json.Obj("""
            {
              "activeProblems": [],
              "currentMedicines": [],
              "allergies": [],
              "lastVitals": null,
              "activePregnancy": null,
              "lastVisitAt": null,
              "visitCount": 0
            }
            """));
    }

    [Fact]
    public async Task Detail_for_Ram_labels_active_problems_from_the_codelist()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var summary = (await _api.Get($"/patients/{RamId}", owner.AccessToken)).Data()["summary"]!;

        JsonAssert.DeepEqual(summary, Json.Obj("""
            {
              "activeProblems": [
                { "code": "E11", "labelEn": "Type 2 diabetes", "labelNp": "मधुमेह", "since": null },
                { "code": "I10", "labelEn": "Essential hypertension", "labelNp": "उच्च रक्तचाप", "since": null }
              ],
              "currentMedicines": [],
              "allergies": ["penicillin"],
              "lastVitals": null,
              "activePregnancy": null,
              "lastVisitAt": null,
              "visitCount": 0
            }
            """));
    }

    [Fact]
    public async Task Detail_is_forbidden_to_other_patients_and_to_health_workers_without_a_grant()
    {
        var stranger = await TestUsers.NewAccount(_api);
        (await _api.Get($"/patients/{SitaId}", stranger.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");

        var worker = await TestUsers.NewHealthWorker(_api, "FCHV-W5-01");
        (await _api.Get($"/patients/{SitaId}", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Detail_of_an_unknown_patient_is_not_found()
    {
        var owner = await TestUsers.NewAccount(_api);
        (await _api.Get($"/patients/{NewId()}", owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- PATCH /patients/:id

    [Fact]
    public async Task Patch_with_the_current_version_applies_and_increments()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var id = patient["id"]!.GetValue<string>();

        var updated = (await _api.Send(HttpMethod.Patch, $"/patients/{id}", Json.Obj("""
            { "version": 1, "allergies": ["penicillin"], "emergencyContactPhone": "+9779801000009" }
            """), owner.AccessToken)).Data()["patient"]!;

        var expected = patient.DeepClone().AsObject();
        expected["allergies"] = new JsonArray("penicillin");
        expected["version"] = 2;
        JsonAssert.DeepEqual(updated, expected, "updatedAt");
        DateTime.Parse(updated["updatedAt"]!.GetValue<string>()).Should()
            .BeAfter(DateTime.Parse(patient["updatedAt"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Patch_with_a_stale_version_is_a_409_carrying_the_current_row()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var id = patient["id"]!.GetValue<string>();
        var v2 = (await _api.Patch($"/patients/{id}", new { version = 1, municipality = "Tulsipur" }, owner.AccessToken))
            .Data()["patient"]!;

        var details = (await _api.Patch($"/patients/{id}", new { version = 1, municipality = "Lamahi" }, owner.AccessToken))
            .Details(HttpStatusCode.Conflict, "VERSION_CONFLICT");

        JsonAssert.HasExactKeys(details, "current");
        JsonAssert.HasExactKeys(details["current"], PatientKeys);
        JsonAssert.DeepEqual(details["current"], v2);
    }

    [Fact]
    public async Task Concurrent_patches_with_the_same_version_let_exactly_one_win()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var id = patient["id"]!.GetValue<string>();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            _api.Patch($"/patients/{id}", new { version = 1, municipality = $"Ward office {i}" }, owner.AccessToken)));

        results.Count(r => r.Status == HttpStatusCode.OK).Should().Be(1);
        results.Where(r => r.Status != HttpStatusCode.OK).Should()
            .OnlyContain(r => r.Status == HttpStatusCode.Conflict && r.Raw.Contains("VERSION_CONFLICT"));
        var detail = (await _api.Get($"/patients/{id}", owner.AccessToken)).Data()["patient"]!;
        detail["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task Patch_distinguishes_explicit_null_from_omitted()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var id = patient["id"]!.GetValue<string>();

        var updated = (await _api.Send(HttpMethod.Patch, $"/patients/{id}",
            Json.Obj("""{ "version": 1, "bloodGroup": null, "ward": null }"""), owner.AccessToken)).Data()["patient"]!;

        updated["bloodGroup"].Should().BeNull();
        updated["ward"].Should().BeNull();
        updated["municipality"]!.GetValue<string>().Should().Be("Ghorahi", "omitted = unchanged");
        updated["emergencyContactPhone"]!.GetValue<string>().Should().Be("+9779801000009");
    }

    [Theory]
    [InlineData("""{ "allergies": [] }""", "version")]
    [InlineData("""{ "version": 1, "name": "" }""", "name")]
    [InlineData("""{ "version": 1, "name": null }""", "name")]
    [InlineData("""{ "version": 1, "sex": "boy" }""", "sex")]
    [InlineData("""{ "version": 1, "dob": null }""", "dob")]
    [InlineData("""{ "version": 1, "allergies": null }""", "allergies")]
    [InlineData("""{ "version": 1, "emergencyContactPhone": "98" }""", "emergencyContactPhone")]
    public async Task Patch_validates_fields(string body, string field)
    {
        var (owner, patient) = await CreateOwnedPatient();
        var details = (await _api.Send(HttpMethod.Patch, $"/patients/{patient["id"]}", Json.Obj(body), owner.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        details.ContainsKey(field).Should().BeTrue(details.ToJsonString());
    }

    [Fact]
    public async Task Patch_is_owner_only()
    {
        var (_, patient) = await CreateOwnedPatient();
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _api.Patch($"/patients/{patient["id"]}", new { version = 1, name = "X" }, worker.AccessToken))
            .Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Patch_of_an_unknown_patient_is_not_found()
    {
        var owner = await TestUsers.NewAccount(_api);
        (await _api.Patch($"/patients/{NewId()}", new { version = 1, name = "X" }, owner.AccessToken))
            .Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---- GET /patients/:id/timeline

    [Fact]
    public async Task Timeline_is_an_empty_page_with_a_null_cursor()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var data = (await _api.Get($"/patients/{patient["id"]}/timeline?limit=50", owner.AccessToken)).Data();

        JsonAssert.DeepEqual(data, Json.Obj("""{ "items": [], "nextBefore": null }"""));
    }

    [Fact]
    public async Task Timeline_accepts_a_before_cursor()
    {
        var (owner, patient) = await CreateOwnedPatient();
        var data = (await _api.Get($"/patients/{patient["id"]}/timeline?limit=10&before=2026-06-01T00:00:00.000Z", owner.AccessToken))
            .Data();

        JsonAssert.HasExactKeys(data, "items", "nextBefore");
        data["items"]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("?before=yesterday", "before")]
    [InlineData("?before=2026-06-01T00:00:00", "before")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=201", "limit")]
    [InlineData("?limit=ten", "limit")]
    public async Task Timeline_validates_its_query(string query, string field)
    {
        var (owner, patient) = await CreateOwnedPatient();
        (await _api.Get($"/patients/{patient["id"]}/timeline{query}", owner.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey(field).Should().BeTrue();
    }

    [Fact]
    public async Task Timeline_is_forbidden_without_access()
    {
        var stranger = await TestUsers.NewAccount(_api);
        (await _api.Get($"/patients/{RamId}/timeline", stranger.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
