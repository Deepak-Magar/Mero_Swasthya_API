using System.Net;
using MeroSwasthya.ContractTests.Infrastructure;

namespace MeroSwasthya.ContractTests;

/// <summary>A.2 Reminder, A.4 GET /patients/:id/reminders, and the additive done / cancel actions.</summary>
[Collection(ApiCollection.Name)]
public sealed class RemindersTests
{
    public static readonly string[] ReminderKeys =
    [
        "id", "patientId", "pregnancyId", "kind", "dueAt", "channel", "recipientPhone", "recipientRole",
        "messageNp", "messageEn", "status", "sentAt",
    ];

    private readonly ApiClient _api;
    private readonly Scenario _s;

    public RemindersTests(ApiFactory factory)
    {
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
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
}
