using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Sms;
using MeroSwasthya.Shared.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.ContractTests.Infrastructure;

/// <summary>A patient-owning account with one fresh family profile.</summary>
public sealed record Family(Session Owner, string PatientId);

/// <summary>Multi-step flows through the real API (share code → redeem → record), reused by the Grants/Clinical/Audit tests.</summary>
public sealed class Scenario(ApiFactory factory, ApiClient api)
{
    public const string OwnerPin = "4321"; // TestUsers.NewAccount default

    public static string NewPatientId() => $"p_{Guid.NewGuid()}";

    public async Task<Family> NewFamily(string sex = "female", string name = "Maya Tharu")
    {
        var owner = await TestUsers.NewAccount(api, name);
        var id = NewPatientId();
        (await api.Post("/patients", new { id, name, sex, dob = "1995-03-10", allergies = new[] { "sulpha" } }, owner.AccessToken)).Data();
        return new Family(owner, id);
    }

    /// <summary>POST /grants as the owner; returns the whole data object { grant, token, qrPayload }.</summary>
    public async Task<JsonObject> Share(Family family, string? scope = "append", int? ttlMinutes = null, string[]? sections = null)
    {
        var body = new JsonObject { ["patientId"] = family.PatientId };
        if (scope is not null) body["scope"] = scope;
        if (ttlMinutes is not null) body["ttlMinutes"] = ttlMinutes;
        if (sections is not null) body["sections"] = new JsonArray(sections.Select(s => (JsonNode)s).ToArray());
        return (await api.Send(HttpMethod.Post, "/grants", body, family.Owner.AccessToken)).Data();
    }

    public Task<ApiResponse> Redeem(Session worker, string qrPayload, string? pin = null) =>
        api.Post("/grants/redeem", pin is null ? new { qrPayload } : new { qrPayload, pin }, worker.AccessToken);

    /// <summary>A fresh provider (or FCHV) holding an active append grant for the family's patient.</summary>
    public async Task<Session> WorkerWithGrant(Family family, string invite = "HA-GHORAHI-01", string scope = "append")
    {
        var worker = await TestUsers.NewHealthWorker(api, invite);
        var share = await Share(family, scope);
        (await Redeem(worker, share["qrPayload"]!.GetValue<string>())).Data();
        return worker;
    }

    /// <summary>The A.4 visit example body (fresh id).</summary>
    public static JsonObject VisitBody(string? id = null, string visitAt = "2026-09-18T04:05:00.000Z") => Json.Obj($$"""
        {
          "id": "{{id ?? $"v_{Guid.NewGuid()}"}}",
          "visitAt": "{{visitAt}}",
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
              "dose": "1 tab",
              "frequency": "BD",
              "durationDays": 30,
              "instructionsNp": "खाना पछि"
            }
          ],
          "supersedesId": null
        }
        """);

    public static byte[] Jpeg(int size = 1024)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[^2] = 0xFF; bytes[^1] = 0xD9;
        return bytes;
    }

    /// <summary>
    /// Sends bytes the way the app's upload worker does (PUT to uploadUrl with uploadHeaders). URLs on this
    /// API go through the in-memory test server; a MinIO URL goes over a real socket.
    /// </summary>
    public async Task<HttpResponseMessage> SendToUrl(HttpMethod method, string url, byte[]? bytes = null, string contentType = "image/jpeg")
    {
        var uri = new Uri(url);
        var client = uri.Host == "localhost" && uri.Port == 80 ? factory.CreateClient() : new HttpClient();
        using var request = new HttpRequestMessage(method, uri);
        if (bytes is not null)
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        return await client.SendAsync(request);
    }

    /// <summary>presign → PUT → complete; returns the completed document.</summary>
    public async Task<JsonObject> UploadDocument(Session uploader, string patientId, byte[] bytes, string? id = null)
    {
        var docId = id ?? $"d_{Guid.NewGuid()}";
        var presign = (await api.Post("/documents/presign", new
        {
            id = docId, patientId, type = "discharge", title = "Bharatpur Hospital discharge sheet",
            takenAt = "2026-07-02", contentType = "image/jpeg", sizeBytes = bytes.Length,
        }, uploader.AccessToken)).Data();

        using var put = await SendToUrl(HttpMethod.Put, presign["uploadUrl"]!.GetValue<string>(), bytes);
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        return (await api.Post($"/documents/{docId}/complete", null, uploader.AccessToken)).Data()["document"]!.AsObject();
    }

    public Task SetGrantColumn(string grantId, string column, string sqlValue) =>
        factory.ExecuteSqlAsync($"update grants.access_grants set {column} = {sqlValue} where id = @id", ("id", grantId));

    /// <summary>"Time travel": every reminder of the patient became due <paramref name="ago"/> ago.</summary>
    public Task MakeRemindersDue(string patientId, TimeSpan ago) =>
        factory.ExecuteSqlAsync("update reminders.reminders set due_at = @due where patient_id = @patient",
            ("due", DateTime.UtcNow - ago), ("patient", patientId));

    /// <summary>One poll of the delivery worker (switched off in the test host), optionally through another gateway.</summary>
    internal async Task<DispatchResult> DispatchReminders(ISmsSender? sender = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dispatcher = sender is null
            ? services.GetRequiredService<ReminderDispatcher>()
            : new ReminderDispatcher(services.GetRequiredService<RemindersDbContext>(), sender,
                services.GetRequiredService<IClock>(), NullLogger<ReminderDispatcher>.Instance);
        return await dispatcher.DispatchDueAsync(CancellationToken.None);
    }

    /// <summary>What the mock SMS outbox holds for one phone, newest first.</summary>
    internal List<SentSms> Outbox(string phone) =>
        factory.Services.GetRequiredService<MockSmsSender>().Sent().Where(m => m.To == phone).ToList();

    public async Task<List<string>> AuditActions(Family family) =>
        (await api.Get($"/patients/{family.PatientId}/audit", family.Owner.AccessToken)).Data()["items"]!.AsArray()
        .Select(i => i!["action"]!.GetValue<string>()).ToList();
}
