using System.Net;
using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>
/// A.2 Reminder, A.4 GET /patients/:id/reminders, the additive done / cancel actions, and the A.5
/// <c>reminders</c> schedule: registering a pregnancy and a visit with <c>followUpAt</c> create them,
/// recording the contact / closing the pregnancy cancels what is still pending.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RemindersTests
{
    public static readonly string[] ReminderKeys =
    [
        "id", "patientId", "pregnancyId", "kind", "dueAt", "channel", "recipientPhone", "recipientRole",
        "messageNp", "messageEn", "status", "sentAt",
    ];

    private const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    private const string RamId = "p_a1a1a1a1-0000-4000-8000-000000000002";
    private const string SitaFamilyPhone = "+9779801000009";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public RemindersTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    /// <summary>09:00 Asia/Kathmandu on that day = 03:15 UTC (A.5).</summary>
    private static string SendAt(DateOnly day) => $"{day:yyyy-MM-dd}T03:15:00.000Z";

    private static string Text(JsonNode? reminder, string key) => reminder![key]!.GetValue<string>();

    private async Task<List<JsonObject>> Reminders(string patientId, Session who) =>
        (await _api.Get($"/patients/{patientId}/reminders?limit=200", who.AccessToken)).Data()["items"]!.AsArray()
        .Select(i => i!.AsObject()).ToList();

    /// <summary>The reminders about one ANC contact, in list order (anc_due first, then the two anc_missed).</summary>
    private static List<JsonObject> About(IEnumerable<JsonObject> reminders, int contactNo) =>
        reminders.Where(r => Text(r, "messageEn").Contains($"ANC contact {contactNo} ")).ToList();

    /// <summary>A family whose profile has an emergency contact (Scenario.NewFamily has none).</summary>
    private async Task<(Family Family, string OwnerPhone, string FamilyPhone)> FamilyWithEmergencyContact()
    {
        var owner = await TestUsers.NewAccount(_api, "Maya Tharu");
        var family = new Family(owner, Scenario.NewPatientId());
        var familyPhone = TestUsers.UniquePhone();
        (await _api.Post("/patients", new
        {
            id = family.PatientId, name = "Maya Tharu", sex = "female", dob = "1995-03-10", emergencyContactPhone = familyPhone,
        }, owner.AccessToken)).Data();
        return (family, Text(owner.User, "phone"), familyPhone);
    }

    private async Task<string> Register(Family family, Session who, DateOnly lmp)
    {
        var id = $"pg_{Guid.NewGuid()}";
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", PregnanciesTests.Body(id, lmp), who.AccessToken)).Data();
        return id;
    }

    private Task<ApiResponse> Record(string pregnancyId, int contactNo, Session who) =>
        _api.Send(HttpMethod.Put, $"/pregnancies/{pregnancyId}/contacts/{contactNo}",
            Json.Obj("""{ "findings": { "bpSys": 110, "bpDia": 70 }, "dangerSigns": [] }"""), who.AccessToken);

    private async Task<JsonObject> Visit(Family family, Session who, DateOnly? followUpAt, string? supersedesId = null, string? id = null)
    {
        var body = Scenario.VisitBody(id);
        body["followUpAt"] = followUpAt?.ToString("yyyy-MM-dd");
        body["supersedesId"] = supersedesId;
        return (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", body, who.AccessToken)).Data()["visit"]!.AsObject();
    }

    [Fact]
    public async Task A_new_patient_has_an_empty_list_in_the_items_shape()
    {
        var family = await _s.NewFamily();
        var data = (await _api.Get($"/patients/{family.PatientId}/reminders", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(data, "items");
        data["items"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_list_follows_the_patient_access_rules()
    {
        var family = await _s.NewFamily();
        var stranger = await TestUsers.NewAccount(_api);
        var worker = await TestUsers.NewHealthWorker(_api);
        (await _api.Get($"/patients/{family.PatientId}/reminders", stranger.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/patients/{family.PatientId}/reminders", worker.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get($"/patients/{family.PatientId}/reminders")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        (await _api.Get($"/patients/p_{Guid.NewGuid()}/reminders", family.Owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
        (await _api.Get($"/patients/{family.PatientId}/reminders?limit=0", family.Owner.AccessToken))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey("limit").Should().BeTrue();

        var granted = await _s.WorkerWithGrant(family, scope: "read");
        (await _api.Get($"/patients/{family.PatientId}/reminders", granted.AccessToken)).Data();
    }

    [Fact]
    public async Task Done_and_cancel_on_an_unknown_reminder_are_not_found()
    {
        var owner = await TestUsers.NewAccount(_api);
        (await _api.Post($"/reminders/rm_{Guid.NewGuid()}/done", null, owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
        (await _api.Post($"/reminders/rm_{Guid.NewGuid()}/cancel", null, owner.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
        (await _api.Post($"/reminders/rm_{Guid.NewGuid()}/cancel")).Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Registering_a_pregnancy_schedules_due_and_missed_for_every_contact_to_the_patient_and_the_emergency_contact()
    {
        var (family, ownerPhone, familyPhone) = await FamilyWithEmergencyContact();
        var owner = family.Owner;

        // Week 10 today: all eight contacts, and every message about them, are still ahead.
        var lmp = Today.AddDays(-70);
        var body = PregnanciesTests.Body(lmp: lmp);
        var pregnancyId = Text(body, "id");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", body, owner.AccessToken)).Data();

        var reminders = await Reminders(family.PatientId, owner);
        reminders.Should().HaveCount(8 * 3 * 2, "8 contacts x (anc_due + anc_missed after 3 and 7 days) x 2 recipients");
        foreach (var r in reminders)
        {
            JsonAssert.HasExactKeys(r, ReminderKeys);
            Text(r, "id").Should().StartWith("rm_");
            Text(r, "patientId").Should().Be(family.PatientId);
            Text(r, "pregnancyId").Should().Be(pregnancyId);
            Text(r, "channel").Should().Be("sms");
            Text(r, "status").Should().Be("pending");
            r["sentAt"].Should().BeNull();
        }
        reminders.Count(r => Text(r, "kind") == "anc_due").Should().Be(16);
        reminders.Count(r => Text(r, "kind") == "anc_missed").Should().Be(32);
        reminders.Select(r => Text(r, "dueAt")).Should().BeInAscendingOrder(StringComparer.Ordinal);
        reminders.Where(r => Text(r, "recipientRole") == "patient").Should().HaveCount(24)
            .And.OnlyContain(r => Text(r, "recipientPhone") == ownerPhone);
        reminders.Where(r => Text(r, "recipientRole") == "family").Should().HaveCount(24)
            .And.OnlyContain(r => Text(r, "recipientPhone") == familyPhone);

        // Contact 1 (week 12): due the day before, missed 3 and 7 days after, all at 09:00 Nepal time.
        var due = lmp.AddDays(84);
        var first = About(reminders, 1);
        first.Select(r => (Text(r, "kind"), Text(r, "dueAt"))).Distinct().Should().Equal(
            ("anc_due", SendAt(due.AddDays(-1))),
            ("anc_missed", SendAt(due.AddDays(3))),
            ("anc_missed", SendAt(due.AddDays(7))));
        // Registered by the owner herself: no facility to name.
        Text(first[0], "messageEn").Should().Be($"Maya Tharu's ANC contact 1 is due on {due:yyyy-MM-dd}.");
        Text(first[0], "messageNp").Should().StartWith("Maya Tharu को १ औं गर्भ जाँच २०").And.EndWith(" मा छ।");
        Text(first[^1], "messageEn").Should()
            .Be($"Maya Tharu missed ANC contact 1 (due {due:yyyy-MM-dd}). Please visit a health facility as soon as possible.");

        // GET /pregnancies/:id carries the same rows; an idempotent re-post schedules nothing twice.
        var bundle = (await _api.Get($"/pregnancies/{pregnancyId}", owner.AccessToken)).Data();
        JsonAssert.DeepEqual(bundle["reminders"], new JsonArray(reminders.Select(r => r.DeepClone()).ToArray()));
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/pregnancies", body, owner.AccessToken)).Data();
        (await Reminders(family.PatientId, owner)).Should().HaveCount(48);
    }

    [Fact]
    public async Task A_pregnancy_registered_by_a_health_worker_names_the_facility_and_skips_what_is_already_past()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);

        // Week 28 (LMP 200 days ago): contacts 1–3 are long past, contact 4 (week 30) is due in 10 days.
        var lmp = Today.AddDays(-200);
        await Register(family, worker, lmp);

        var reminders = await Reminders(family.PatientId, worker);
        reminders.Should().HaveCount(5 * 3, "contacts 4–8 only, and no emergency contact on this profile");
        reminders.Should().OnlyContain(r => Text(r, "recipientRole") == "patient" && Text(r, "recipientPhone") == Text(family.Owner.User, "phone"));
        foreach (var past in new[] { 1, 2, 3 }) About(reminders, past).Should().BeEmpty();
        reminders.Select(r => DateTime.Parse(Text(r, "dueAt")).ToUniversalTime()).Min().Should().BeAfter(DateTime.UtcNow);

        var due = lmp.AddDays(210);
        var fourth = About(reminders, 4);
        Text(fourth[0], "dueAt").Should().Be(SendAt(due.AddDays(-1)));
        Text(fourth[0], "messageEn").Should().Be($"Maya Tharu's ANC contact 4 is due on {due:yyyy-MM-dd} at Ghorahi Health Post.");
        Text(fourth[0], "messageNp").Should().StartWith("Maya Tharu को ४ औं गर्भ जाँच २०").And.EndWith(" मा Ghorahi Health Post मा छ।");
        Text(fourth[1], "messageEn").Should()
            .Be($"Maya Tharu missed ANC contact 4 (due {due:yyyy-MM-dd}). Please visit Ghorahi Health Post as soon as possible.");
    }

    [Fact]
    public async Task Recording_a_contact_cancels_its_pending_reminders_and_leaves_the_others()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var pregnancyId = await Register(family, worker, Today.AddDays(-200));
        About(await Reminders(family.PatientId, worker), 5).Should().HaveCount(3);

        (await Record(pregnancyId, 5, worker)).Data();

        var after = await Reminders(family.PatientId, worker);
        About(after, 5).Should().BeEmpty("A.4: cancels pending anc_missed reminder for this contact (and its anc_due)");
        after.Should().HaveCount(4 * 3);
        (await _api.Get($"/pregnancies/{pregnancyId}", worker.AccessToken)).Data()["reminders"]!.AsArray().Should().HaveCount(12);

        // Recording it again (a correction) changes nothing.
        (await Record(pregnancyId, 5, worker)).Data();
        (await Reminders(family.PatientId, worker)).Should().HaveCount(12);
    }

    [Fact]
    public async Task Delivery_and_ending_a_pregnancy_cancel_everything_still_pending()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var delivered = await Register(family, worker, Today.AddDays(-200));
        (await Reminders(family.PatientId, worker)).Should().HaveCount(15);
        (await _api.Send(HttpMethod.Post, $"/pregnancies/{delivered}/delivery", DeliveriesTests.Body(), worker.AccessToken)).Data();
        (await Reminders(family.PatientId, worker)).Should().BeEmpty();
        (await _api.Get($"/pregnancies/{delivered}", worker.AccessToken)).Data()["reminders"]!.AsArray().Should().BeEmpty();

        var other = await _s.NewFamily();
        var ended = await Register(other, other.Owner, Today.AddDays(-200));
        (await Reminders(other.PatientId, other.Owner)).Should().HaveCount(15);
        (await _api.Patch($"/pregnancies/{ended}", new { version = 1, status = "ended" }, other.Owner.AccessToken)).Data();
        (await Reminders(other.PatientId, other.Owner)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_visit_with_followUpAt_schedules_one_follow_up_the_day_before_to_the_patient()
    {
        var (family, ownerPhone, _) = await FamilyWithEmergencyContact();
        var worker = await _s.WorkerWithGrant(family);
        var followUpAt = Today.AddDays(10);

        var visit = await Visit(family, worker, followUpAt);

        var reminder = (await Reminders(family.PatientId, family.Owner)).Should().ContainSingle("A.5: to patient phone only").Subject;
        JsonAssert.DeepEqual(reminder, Json.Obj($$"""
            {
              "id": "<ignored>",
              "patientId": "{{family.PatientId}}",
              "pregnancyId": null,
              "kind": "follow_up",
              "dueAt": "{{SendAt(followUpAt.AddDays(-1))}}",
              "channel": "sms",
              "recipientPhone": "{{ownerPhone}}",
              "recipientRole": "patient",
              "messageNp": "<ignored>",
              "messageEn": "Maya Tharu's follow-up visit is due on {{followUpAt:yyyy-MM-dd}} at Ghorahi Health Post.",
              "status": "pending",
              "sentAt": null
            }
            """), "id", "messageNp");
        Text(reminder, "messageNp").Should().StartWith("Maya Tharu को फलो-अप जाँच २०").And.EndWith(" मा Ghorahi Health Post मा छ।");

        // The same visit again (outbox retry), a visit without followUpAt, and one whose follow-up is already past add nothing.
        await Visit(family, worker, followUpAt, id: Text(visit, "id"));
        await Visit(family, worker, followUpAt: null);
        await Visit(family, worker, new DateOnly(2026, 9, 20));
        (await Reminders(family.PatientId, family.Owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_self_reported_follow_up_has_no_facility_and_a_correction_replaces_the_reminder()
    {
        var family = await _s.NewFamily();
        var first = await Visit(family, family.Owner, Today.AddDays(10));
        var reminder = (await Reminders(family.PatientId, family.Owner)).Should().ContainSingle().Subject;
        Text(reminder, "messageEn").Should().Be($"Maya Tharu's follow-up visit is due on {Today.AddDays(10):yyyy-MM-dd}.");

        await Visit(family, family.Owner, Today.AddDays(20), supersedesId: Text(first, "id"));

        var replaced = (await Reminders(family.PatientId, family.Owner)).Should().ContainSingle("the superseded visit's follow-up is cancelled").Subject;
        Text(replaced, "dueAt").Should().Be(SendAt(Today.AddDays(19)));
        Text(replaced, "id").Should().NotBe(Text(reminder, "id"));
    }

    [Fact]
    public async Task Seed_Sitas_contact_4_reminders_were_sent_yesterday_and_the_rest_are_pending()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var reminders = await Reminders(SitaId, owner);

        // Contact 4 is due on the seed day (week 30), so its anc_due went out yesterday at 09:00 Nepal time — the A.2 example.
        var sent = reminders.Where(r => Text(r, "status") == "sent").ToList();
        sent.Select(r => (Text(r, "recipientPhone"), Text(r, "recipientRole"))).Should()
            .BeEquivalentTo([(TestUsers.PatientPhone, "patient"), (SitaFamilyPhone, "family")]);
        foreach (var r in sent)
        {
            JsonAssert.HasExactKeys(r, ReminderKeys);
            Text(r, "kind").Should().Be("anc_due");
            Text(r, "dueAt").Should().Be(SendAt(Today.AddDays(-1)));
            Text(r, "sentAt").Should().Be($"{Today.AddDays(-1):yyyy-MM-dd}T03:15:05.000Z");
            Text(r, "messageEn").Should().Be($"Sita Chaudhary's ANC contact 4 is due on {Today:yyyy-MM-dd} at Ghorahi Health Post.");
            Text(r, "messageNp").Should().StartWith("Sita Chaudhary को ४ औं गर्भ जाँच २०").And.EndWith(" मा Ghorahi Health Post मा छ।");
        }

        var pending = reminders.Where(r => Text(r, "status") == "pending").ToList();
        pending.Should().HaveCount(2 * 2 + 4 * 3 * 2, "contact 4: anc_missed x 2 recipients; contacts 5–8: all three x 2 recipients");
        About(pending, 4).Should().HaveCount(4).And.OnlyContain(r => Text(r, "kind") == "anc_missed");
        foreach (var done in new[] { 1, 2, 3 }) About(reminders, done).Should().BeEmpty();
    }

    [Fact]
    public async Task Seed_Rams_latest_visit_has_a_pending_follow_up()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var followUpAt = Today.AddDays(-25).AddDays(30);

        var reminder = (await Reminders(RamId, owner))
            .Where(r => Text(r, "kind") == "follow_up" && Text(r, "dueAt") == SendAt(followUpAt.AddDays(-1)))
            .Should().ContainSingle().Subject;
        Text(reminder, "recipientPhone").Should().Be(TestUsers.PatientPhone);
        Text(reminder, "recipientRole").Should().Be("patient");
        reminder["pregnancyId"].Should().BeNull();
        Text(reminder, "status").Should().Be("pending");
        Text(reminder, "messageEn").Should()
            .Be($"Ram Bahadur Chaudhary's follow-up visit is due on {followUpAt:yyyy-MM-dd} at Ghorahi Health Post.");
    }
}
