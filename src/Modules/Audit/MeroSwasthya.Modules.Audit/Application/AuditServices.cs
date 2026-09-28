using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Audit.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Audit.Application;

internal sealed class AuditWriter(AuditDbContext db, ICurrentUser currentUser, IClock clock) : IAuditWriter
{
    public async Task WriteAsync(string patientId, AuditAction action, CancellationToken ct = default) =>
        await WriteAsync(patientId, action, await currentUser.GetAsync(ct), ct);

    internal async Task WriteAsync(string patientId, AuditAction action, CurrentUserInfo actor, CancellationToken ct)
    {
        db.Entries.Add(new AuditEntry
        {
            Id = Ids.New("au"),
            PatientId = patientId,
            ActorUserId = actor.Id,
            ActorName = actor.Name,
            ActorFacilityName = actor.FacilityName,
            Action = action,
            At = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary><c>record_viewed</c> at most once per 10 minutes per (actor, patient), so a scrolling provider is one line.</summary>
internal sealed class RecordViewedObserver(AuditDbContext db, AuditWriter writer, IClock clock) : IPatientReadObserver
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public async Task OnRecordViewedAsync(PatientDto patient, CurrentUserInfo reader, CancellationToken ct = default)
    {
        var since = clock.UtcNow - Window;
        var recent = await db.Entries.AnyAsync(e =>
            e.ActorUserId == reader.Id && e.PatientId == patient.Id &&
            e.Action == AuditAction.RecordViewed && e.At > since, ct);
        if (!recent) await writer.WriteAsync(patient.Id, AuditAction.RecordViewed, reader, ct);
    }
}

internal sealed class AuditQueries(AuditDbContext db)
{
    public async Task<IReadOnlyList<AuditEntryDto>> ForPatientAsync(string patientId, int limit, CancellationToken ct) =>
        await db.Entries.AsNoTracking()
            .Where(e => e.PatientId == patientId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Seq)
            .Take(limit)
            .Select(e => new AuditEntryDto(e.Id, e.PatientId, e.ActorUserId, e.ActorName, e.ActorFacilityName, e.Action, e.At))
            .ToListAsync(ct);
}
