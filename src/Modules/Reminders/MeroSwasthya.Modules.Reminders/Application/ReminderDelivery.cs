using MeroSwasthya.Modules.Reminders.Domain;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Sms;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Modules.Reminders.Application;

/// <summary>Bound from <c>Reminders</c>.</summary>
internal sealed class ReminderDeliveryOptions
{
    public const string Section = "Reminders";

    /// <summary>False = reminders are scheduled but nothing polls them (the contract tests drive the dispatcher themselves).</summary>
    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
}

internal sealed record DispatchResult(int Sent, int Retrying, int Failed)
{
    public int Total => Sent + Retrying + Failed;
}

/// <summary>
/// Sends the reminders that are due: pending, not cancelled, <c>dueAt</c> reached. The Nepali text goes
/// out (A.4 /demo/sms). A send that throws is tried again on the next poll, <see cref="MaxAttempts"/>
/// times in all, then the reminder is <c>failed</c>. A reminder found more than
/// <see cref="MaxLateness"/> after its <c>dueAt</c> (the server was down) is <c>failed</c> unsent —
/// "your check-up is tomorrow" must not arrive days late.
/// </summary>
internal sealed class ReminderDispatcher(RemindersDbContext db, ISmsSender sms, IClock clock, ILogger<ReminderDispatcher> logger)
{
    public const int BatchSize = 100;
    public const int MaxAttempts = 3;
    public const string ExpiredError = "Not sent within 24 h of dueAt";

    public static readonly TimeSpan MaxLateness = TimeSpan.FromHours(24);

    public async Task<DispatchResult> DispatchDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Reminders
            .Where(r => r.Status == ReminderStatus.Pending && r.CancelledAt == null
                        && r.Channel == ReminderChannel.Sms && r.DueAt <= now)
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        int sent = 0, retrying = 0, failed = 0;
        foreach (var reminder in due)
        {
            if (now - reminder.DueAt > MaxLateness)
            {
                reminder.Status = ReminderStatus.Failed;
                reminder.LastError = ExpiredError;
                failed++;
            }
            else
            {
                reminder.Attempts++;
                try
                {
                    await sms.SendAsync(reminder.RecipientPhone, reminder.MessageNp, ct);
                    reminder.Status = ReminderStatus.Sent;
                    reminder.SentAt = clock.UtcNow;
                    reminder.LastError = null;
                    sent++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    reminder.LastError = e.Message.Length > 500 ? e.Message[..500] : e.Message;
                    if (reminder.Attempts >= MaxAttempts)
                    {
                        reminder.Status = ReminderStatus.Failed;
                        failed++;
                    }
                    else
                    {
                        retrying++;
                    }
                    logger.LogWarning("Reminder {Id} was not sent (attempt {Attempt} of {Max}): {Error}",
                        reminder.Id, reminder.Attempts, MaxAttempts, reminder.LastError);
                }
            }

            // One row at a time: a crash in the middle of a batch never re-sends what already went out.
            reminder.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new DispatchResult(sent, retrying, failed);
    }
}

/// <summary>Polls the due reminders every <see cref="ReminderDeliveryOptions.PollInterval"/> (60 s), starting right after startup.</summary>
internal sealed class ReminderDeliveryWorker(
    IServiceScopeFactory scopes,
    ReminderDeliveryOptions options,
    ILogger<ReminderDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ReminderDispatcher>().DispatchDueAsync(stoppingToken);
                if (result.Total > 0)
                    logger.LogInformation("Reminders: {Sent} sent, {Retrying} to retry, {Failed} failed",
                        result.Sent, result.Retrying, result.Failed);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A database hiccup must not end the loop; the next poll tries again.
                logger.LogError(e, "Reminder delivery poll failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
