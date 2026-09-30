using System.Text.Json.Nodes;
using MeroSwasthya.ContractTests.Infrastructure;
using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Sms;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeroSwasthya.ContractTests;

/// <summary>
/// Delivery of due reminders through <see cref="ISmsSender"/>. The background worker is switched off in
/// the test host (ApiFactory), so each case runs the dispatcher — or its own worker — against a reminder
/// the API scheduled and the test moved into the past.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReminderDeliveryTests
{
    private readonly ApiFactory _factory;
    private readonly ApiClient _api;
    private readonly Scenario _s;

    public ReminderDeliveryTests(ApiFactory factory)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
    }

    private sealed class FailingSender : ISmsSender
    {
        public Task SendAsync(string to, string text, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("gateway timeout"));
    }

    /// <summary>A fresh family whose self-reported visit has a follow-up in 10 days: exactly one pending reminder.</summary>
    private async Task<(Family Family, string Phone)> FamilyWithFollowUp()
    {
        var family = await _s.NewFamily();
        var body = Scenario.VisitBody();
        body["followUpAt"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10).ToString("yyyy-MM-dd");
        (await _api.Send(HttpMethod.Post, $"/patients/{family.PatientId}/visits", body, family.Owner.AccessToken)).Data();
        return (family, family.Owner.User["phone"]!.GetValue<string>());
    }

    private async Task<JsonObject> Reminder(Family family) =>
        (await _api.Get($"/patients/{family.PatientId}/reminders", family.Owner.AccessToken)).Data()["items"]!.AsArray()
        .Should().ContainSingle().Subject!.AsObject();

    /// <summary>"Time travel": the reminder became due <paramref name="ago"/> ago.</summary>
    private Task MakeDue(Family family, TimeSpan ago) =>
        _factory.ExecuteSqlAsync("update reminders.reminders set due_at = @due where patient_id = @patient",
            ("due", DateTime.UtcNow - ago), ("patient", family.PatientId));

    private async Task<DispatchResult> Dispatch(ISmsSender? sender = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dispatcher = sender is null
            ? services.GetRequiredService<ReminderDispatcher>()
            : new ReminderDispatcher(services.GetRequiredService<RemindersDbContext>(), sender,
                services.GetRequiredService<IClock>(), NullLogger<ReminderDispatcher>.Instance);
        return await dispatcher.DispatchDueAsync(CancellationToken.None);
    }

    private List<SentSms> Outbox(string phone) =>
        _factory.Services.GetRequiredService<MockSmsSender>().Sent().Where(m => m.To == phone).ToList();

    [Fact]
    public void The_test_host_sends_through_the_mock_outbox()
    {
        _factory.Services.GetRequiredService<ISmsSender>().Should().BeOfType<MockSmsSender>("Features:SmsMode = mock");
    }

    [Fact]
    public async Task A_due_reminder_is_sent_once_with_the_nepali_text()
    {
        var (family, phone) = await FamilyWithFollowUp();
        var scheduled = await Reminder(family);
        await MakeDue(family, TimeSpan.FromHours(1));

        (await Dispatch()).Sent.Should().BeGreaterThanOrEqualTo(1);

        var sent = await Reminder(family);
        sent["status"]!.GetValue<string>().Should().Be("sent");
        JsonAssert.IsIsoTimestamp(sent["sentAt"]);
        var message = Outbox(phone).Should().ContainSingle().Subject;
        message.Text.Should().Be(scheduled["messageNp"]!.GetValue<string>());
        message.SentAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // The next poll has nothing left to do for it.
        await Dispatch();
        Outbox(phone).Should().ContainSingle();
    }

    [Fact]
    public async Task A_reminder_that_is_not_due_yet_stays_pending()
    {
        var (family, phone) = await FamilyWithFollowUp();

        await Dispatch();

        var reminder = await Reminder(family);
        reminder["status"]!.GetValue<string>().Should().Be("pending");
        reminder["sentAt"].Should().BeNull();
        Outbox(phone).Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_reminder_is_never_sent()
    {
        var (family, phone) = await FamilyWithFollowUp();
        var id = (await Reminder(family))["id"]!.GetValue<string>();
        await MakeDue(family, TimeSpan.FromHours(1));
        (await _api.Post($"/reminders/{id}/cancel", null, family.Owner.AccessToken)).Data();

        await Dispatch();

        Outbox(phone).Should().BeEmpty();
    }

    [Fact]
    public async Task A_reminder_found_more_than_a_day_late_fails_without_being_sent()
    {
        var (family, phone) = await FamilyWithFollowUp();
        await MakeDue(family, TimeSpan.FromHours(25));

        (await Dispatch()).Failed.Should().BeGreaterThanOrEqualTo(1);

        var reminder = await Reminder(family);
        reminder["status"]!.GetValue<string>().Should().Be("failed");
        reminder["sentAt"].Should().BeNull();
        Outbox(phone).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_gateway_is_retried_three_times_and_then_the_reminder_is_failed()
    {
        var (family, phone) = await FamilyWithFollowUp();
        var id = (await Reminder(family))["id"]!.GetValue<string>();
        await MakeDue(family, TimeSpan.FromHours(1));
        var gateway = new FailingSender();

        (await Dispatch(gateway)).Retrying.Should().Be(1);
        (await Dispatch(gateway)).Retrying.Should().Be(1);
        (await Reminder(family))["status"]!.GetValue<string>().Should().Be("pending", "two attempts so far");

        (await Dispatch(gateway)).Failed.Should().Be(1);
        var failed = await Reminder(family);
        failed["status"]!.GetValue<string>().Should().Be("failed");
        failed["sentAt"].Should().BeNull();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<RemindersDbContext>().Reminders.AsNoTracking().SingleAsync(r => r.Id == id);
            row.Attempts.Should().Be(ReminderDispatcher.MaxAttempts);
            row.LastError.Should().Be("gateway timeout");
        }

        // A failed reminder is final: a working gateway later does not send it.
        await Dispatch();
        Outbox(phone).Should().BeEmpty();
    }

    [Fact]
    public async Task The_background_worker_keeps_polling_and_sends_what_becomes_due()
    {
        var (first, firstPhone) = await FamilyWithFollowUp();
        var (second, secondPhone) = await FamilyWithFollowUp();
        await MakeDue(first, TimeSpan.FromMinutes(5));

        using var worker = new ReminderDeliveryWorker(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new ReminderDeliveryOptions { PollInterval = TimeSpan.FromMilliseconds(50) },
            NullLogger<ReminderDeliveryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(() => Outbox(firstPhone).Count == 1, "the first poll sends what is already due");
            Outbox(secondPhone).Should().BeEmpty();

            await MakeDue(second, TimeSpan.FromMinutes(5));
            await Eventually(() => Outbox(secondPhone).Count == 1, "a later poll sends what became due meanwhile");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        (await Reminder(first))["status"]!.GetValue<string>().Should().Be("sent");
        (await Reminder(second))["status"]!.GetValue<string>().Should().Be("sent");
        Outbox(firstPhone).Should().ContainSingle("fifty-millisecond polls must not send it twice");
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        condition().Should().BeTrue(because);
    }
}
