using MeroSwasthya.Modules.Auth.Contracts;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Reminders.Domain;
using MeroSwasthya.Modules.Reminders.Infrastructure;
using MeroSwasthya.Shared.Events;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeroSwasthya.Modules.Reminders.Application;

internal sealed record ReminderRecipient(string Phone, RecipientRole Role);

/// <summary>
/// Turns what happened in Maternal and Clinical into <see cref="Reminder"/> rows (A.5 <c>reminders</c>).
/// Only messages whose send time is still ahead are created — a contact or follow-up that is already
/// past never produces a late SMS. Scheduling is idempotent on (<c>sourceKey</c>, recipient), so an
/// event delivered twice, or a re-run seed, adds nothing.
/// </summary>
internal sealed class ReminderScheduler(
    RemindersDbContext db,
    IPregnancyDirectory pregnancies,
    IPatientDirectory patients,
    IUserDirectory users,
    IClock clock)
{
    /// <summary>
    /// <c>anc_due</c> and <c>anc_missed</c> for every contact of an active pregnancy that is not recorded
    /// yet, to the patient's phone and the emergency contact (A.5). The facility in the text is the one of
    /// the health worker who registered the pregnancy.
    /// </summary>
    public async Task SchedulePregnancyAsync(string pregnancyId, CancellationToken ct)
    {
        var plan = await PlanPregnancyAsync(pregnancyId, ct);
        if (plan is null) return;
        await AddMissingAsync(plan.Value.Patient.Id, pregnancyId, plan.Value.Planned.Where(p => p.DueAt > clock.UtcNow), plan.Value.Recipients, sent: false, ct);
    }

    /// <summary>
    /// Seed only: the <c>anc_due</c> of the next contact when its send time has already passed, stored as
    /// sent — the A.2 example (Sita's contact is due today, the SMS went out yesterday at 09:00).
    /// </summary>
    public async Task BackfillSentAncDueAsync(string pregnancyId, CancellationToken ct)
    {
        var plan = await PlanPregnancyAsync(pregnancyId, ct);
        var next = plan?.Planned.FirstOrDefault(p => p.Kind == ReminderKind.AncDue);
        if (next is null || next.DueAt > clock.UtcNow) return;
        await AddMissingAsync(plan!.Value.Patient.Id, pregnancyId, [next], plan.Value.Recipients, sent: true, ct);
    }

    /// <summary>A.5 <c>follow_up</c>: the day before <c>Visit.followUpAt</c>, to the patient's phone.</summary>
    public async Task ScheduleFollowUpAsync(string visitId, string patientId, DateOnly followUpAt, string providerUserId, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null) return;

        var facility = (await users.FindAsync(providerUserId, ct))?.FacilityName;
        var planned = ReminderPlanner.ForFollowUp(patient.Name, visitId, followUpAt, facility);
        if (planned.DueAt <= clock.UtcNow) return;
        await AddMissingAsync(patientId, null, [planned], await RecipientsAsync(patient, family: false, ct), sent: false, ct);
    }

    /// <summary>The contact happened: its pending <c>anc_due</c> / <c>anc_missed</c> must not go out.</summary>
    public Task<int> CancelForContactAsync(string contactId, CancellationToken ct)
    {
        var prefix = ReminderPlanner.ContactPrefix(contactId);
        return CancelAsync(db.Reminders.Where(r => r.SourceKey.StartsWith(prefix)), ct);
    }

    /// <summary>The pregnancy is over (delivered or ended): nothing about it is due any more.</summary>
    public Task<int> CancelForPregnancyAsync(string pregnancyId, CancellationToken ct) =>
        CancelAsync(db.Reminders.Where(r => r.PregnancyId == pregnancyId), ct);

    /// <summary>The visit was corrected by another one, which schedules its own follow-up.</summary>
    public Task<int> CancelForVisitAsync(string visitId, CancellationToken ct)
    {
        var key = ReminderPlanner.VisitKey(visitId);
        return CancelAsync(db.Reminders.Where(r => r.SourceKey == key), ct);
    }

    private async Task<(PatientDto Patient, IReadOnlyList<ReminderRecipient> Recipients, IReadOnlyList<PlannedReminder> Planned)?> PlanPregnancyAsync(
        string pregnancyId, CancellationToken ct)
    {
        var snapshot = await pregnancies.FindAsync(pregnancyId, ct);
        if (snapshot is null || snapshot.Pregnancy.Status != PregnancyStatus.Active) return null;
        var patient = await patients.FindAsync(snapshot.Pregnancy.PatientId, ct);
        if (patient is null) return null;

        var facility = (await users.FindAsync(snapshot.Pregnancy.RegisteredByUserId, ct))?.FacilityName;
        var planned = snapshot.Contacts
            .Where(c => c.DoneAt is null && !c.Deleted)
            .OrderBy(c => c.ContactNo)
            .SelectMany(c => ReminderPlanner.ForContact(patient.Name, c.Id, c.ContactNo, c.DueAt, facility))
            .ToList();
        return (patient, await RecipientsAsync(patient, family: true, ct), planned);
    }

    /// <summary>
    /// "Patient phone" is the phone of the account that owns the profile (a Patient has no phone of its
    /// own); the family recipient is <c>emergencyContactPhone</c>, unless it is that same number.
    /// </summary>
    private async Task<IReadOnlyList<ReminderRecipient>> RecipientsAsync(PatientDto patient, bool family, CancellationToken ct)
    {
        var recipients = new List<ReminderRecipient>();
        if (await users.FindAsync(patient.OwnerUserId, ct) is { } owner)
            recipients.Add(new ReminderRecipient(owner.Phone, RecipientRole.Patient));
        if (family && patient.EmergencyContactPhone is { Length: > 0 } phone && recipients.All(r => r.Phone != phone))
            recipients.Add(new ReminderRecipient(phone, RecipientRole.Family));
        return recipients;
    }

    private async Task AddMissingAsync(
        string patientId, string? pregnancyId, IEnumerable<PlannedReminder> planned, IReadOnlyList<ReminderRecipient> recipients,
        bool sent, CancellationToken ct)
    {
        var wanted = planned.ToList();
        if (wanted.Count == 0 || recipients.Count == 0) return;
        var keys = wanted.Select(p => p.SourceKey).ToList();

        // Second attempt only after a concurrent request scheduled some of the same rows first.
        for (var attempt = 1; ; attempt++)
        {
            var existing = (await db.Reminders.AsNoTracking()
                    .Where(r => keys.Contains(r.SourceKey))
                    .Select(r => new { r.SourceKey, r.RecipientPhone })
                    .ToListAsync(ct))
                .Select(r => (r.SourceKey, r.RecipientPhone))
                .ToHashSet();

            var now = clock.UtcNow;
            foreach (var p in wanted)
            foreach (var recipient in recipients)
            {
                if (existing.Contains((p.SourceKey, recipient.Phone))) continue;
                db.Reminders.Add(new Reminder
                {
                    Id = Ids.New("rm"),
                    PatientId = patientId,
                    PregnancyId = pregnancyId,
                    Kind = p.Kind,
                    DueAt = p.DueAt,
                    Channel = ReminderChannel.Sms,
                    RecipientPhone = recipient.Phone,
                    RecipientRole = recipient.Role,
                    MessageNp = p.MessageNp,
                    MessageEn = p.MessageEn,
                    Status = sent ? ReminderStatus.Sent : ReminderStatus.Pending,
                    SentAt = sent ? p.DueAt.AddSeconds(5) : null,
                    SourceKey = p.SourceKey,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException e) when (attempt == 1 && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Cancel = <c>cancelledAt</c> (A.2 has no "cancelled" status). Only what is still pending; a sent message stays in the list.</summary>
    private Task<int> CancelAsync(IQueryable<Reminder> reminders, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return reminders
            .Where(r => r.Status == ReminderStatus.Pending && r.CancelledAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelledAt, now).SetProperty(r => r.UpdatedAt, now), ct);
    }
}

/// <summary>A.4 POST /patients/:id/pregnancies: "creates 8 AncContacts with deterministic ids and the Reminders".</summary>
internal sealed class PregnancyRegisteredHandler(ReminderScheduler scheduler) : IDomainEventHandler<PregnancyRegistered>
{
    public Task HandleAsync(PregnancyRegistered domainEvent, CancellationToken ct) =>
        scheduler.SchedulePregnancyAsync(domainEvent.PregnancyId, ct);
}

/// <summary>
/// A.4 PUT /pregnancies/:id/contacts/:contactNo: "Cancels pending anc_missed reminder for this contact" —
/// and its <c>anc_due</c>, when she came before the reminder went out.
/// </summary>
internal sealed class AncContactRecordedHandler(ReminderScheduler scheduler) : IDomainEventHandler<AncContactRecorded>
{
    public Task HandleAsync(AncContactRecorded domainEvent, CancellationToken ct) =>
        scheduler.CancelForContactAsync(domainEvent.ContactId, ct);
}

internal sealed class PregnancyClosedHandler(ReminderScheduler scheduler) : IDomainEventHandler<PregnancyClosed>
{
    public Task HandleAsync(PregnancyClosed domainEvent, CancellationToken ct) =>
        scheduler.CancelForPregnancyAsync(domainEvent.PregnancyId, ct);
}

/// <summary>A.4 POST /patients/:id/visits: "Creates Reminder follow_up if followUpAt set."</summary>
internal sealed class FollowUpScheduledHandler(ReminderScheduler scheduler) : IDomainEventHandler<FollowUpScheduled>
{
    public Task HandleAsync(FollowUpScheduled domainEvent, CancellationToken ct) =>
        scheduler.ScheduleFollowUpAsync(domainEvent.VisitId, domainEvent.PatientId, domainEvent.FollowUpAt, domainEvent.ProviderUserId, ct);
}

internal sealed class VisitSupersededHandler(ReminderScheduler scheduler) : IDomainEventHandler<VisitSuperseded>
{
    public Task HandleAsync(VisitSuperseded domainEvent, CancellationToken ct) =>
        scheduler.CancelForVisitAsync(domainEvent.SupersededVisitId, ct);
}
