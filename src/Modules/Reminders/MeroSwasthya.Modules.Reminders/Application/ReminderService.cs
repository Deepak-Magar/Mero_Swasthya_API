using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Reminders.Contracts;
using MeroSwasthya.Modules.Reminders.Domain;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Reminders.Application;

internal static class ReminderMapping
{
    public static ReminderDto ToDto(this Reminder r) => new(
        r.Id, r.PatientId, r.PregnancyId, r.Kind, r.DueAt, r.Channel, r.RecipientPhone, r.RecipientRole,
        r.MessageNp, r.MessageEn, r.Status, r.SentAt);
}

internal sealed record ReminderResponse(ReminderDto Reminder);

/// <summary>A.4 GET /patients/:id/reminders, plus the additive done / cancel actions.</summary>
internal sealed class ReminderService(RemindersDbContext db, IPatientAccess access, IClock clock)
{
    /// <summary>Upcoming and recent, earliest first; cancelled reminders are not shown.</summary>
    public async Task<IReadOnlyList<ReminderDto>> ListAsync(string patientId, int limit, CancellationToken ct)
    {
        await access.RequireReadAsync(patientId, ct);
        var rows = await db.Reminders.AsNoTracking()
            .Where(r => r.PatientId == patientId && r.CancelledAt == null)
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(r => r.ToDto()).ToList();
    }

    /// <summary>The message was delivered another way (e.g. the FCHV phoned): status → sent now.</summary>
    public async Task<ReminderDto> MarkDoneAsync(string id, CancellationToken ct)
    {
        var reminder = await LoadForWriteAsync(id, ct);
        if (reminder.Status != ReminderStatus.Sent)
        {
            reminder.Status = ReminderStatus.Sent;
            reminder.SentAt = clock.UtcNow;
            reminder.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return reminder.ToDto();
    }

    /// <summary>Never send it; the reminder leaves the patient's list. Idempotent; a sent reminder cannot be cancelled.</summary>
    public async Task<ReminderDto> CancelAsync(string id, CancellationToken ct)
    {
        var reminder = await LoadForWriteAsync(id, ct);
        if (reminder.Status == ReminderStatus.Sent)
            throw AppException.RuleViolation("This reminder was already sent");
        if (reminder.CancelledAt is null)
        {
            reminder.CancelledAt = clock.UtcNow;
            reminder.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return reminder.ToDto();
    }

    private async Task<Reminder> LoadForWriteAsync(string id, CancellationToken ct)
    {
        var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound("Reminder");
        await access.RequireAppendAsync(reminder.PatientId, ct);
        return reminder;
    }
}

/// <summary>Fills <c>reminders</c> in GET /pregnancies/:id.</summary>
internal sealed class PregnancyReminderSource(RemindersDbContext db) : IPregnancyReminderSource
{
    public async Task<IReadOnlyList<object>> ForPregnancyAsync(string pregnancyId, CancellationToken ct = default)
    {
        var rows = await db.Reminders.AsNoTracking()
            .Where(r => r.PregnancyId == pregnancyId && r.CancelledAt == null)
            .OrderBy(r => r.DueAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
        return rows.Select(r => (object)r.ToDto()).ToList();
    }
}
