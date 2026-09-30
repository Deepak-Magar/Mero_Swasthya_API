using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Modules.Clinical.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Events;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeroSwasthya.Modules.Clinical.Application;

internal static class ClinicalMapping
{
    public static VisitDto ToDto(this Visit v) => new(
        v.Id, v.PatientId, v.ProviderUserId, v.ProviderName, v.FacilityId, v.FacilityName, v.VisitAt,
        v.ChiefComplaintCode, v.Vitals, v.DiagnosisCodes.ToList(), v.Notes, v.Advice, v.FollowUpAt, v.Referral,
        v.Prescriptions.ToList(), v.SupersedesId, v.Version, v.UpdatedAt, v.Deleted);
}

/// <summary>A.4 "Visits". Append-only: create (idempotent on the client id) and list; no update, no delete.</summary>
internal sealed class VisitService(
    ClinicalDbContext db,
    IPatientAccess access,
    ICurrentUser currentUser,
    ICodeListLookup codes,
    IAuditWriter audit,
    IDomainEventPublisher events,
    IClock clock)
{
    /// <summary>Provider name recorded when the owner writes a visit into their own record.</summary>
    public const string SelfReported = "Self-reported";

    public async Task<VisitDto> CreateAsync(string patientId, CreateVisitRequest request, CancellationToken ct)
    {
        // Owner, or a health worker with an active append grant (403 FORBIDDEN / 403 GRANT_EXPIRED otherwise).
        var patient = await access.RequireAppendAsync(patientId, ct);
        var user = await currentUser.GetAsync(ct);
        var isOwner = patient.OwnerUserId == user.Id;
        if (user.Role == UserRole.Fchv && !isOwner)
            throw AppException.Forbidden("FCHVs cannot record clinical visits");

        var existing = await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.Id, ct);
        if (existing is not null) return Idempotent(existing, patientId);

        if (request.SupersedesId is not null &&
            !await db.Visits.AnyAsync(v => v.Id == request.SupersedesId && v.PatientId == patientId, ct))
            throw AppException.Validation("supersedesId", "No visit with this id for this patient");

        var drugLabels = await codes.LabelsAsync(
            CodeListKind.Drug, request.Prescriptions?.Select(p => p.DrugCode!) ?? [], ct);

        var now = clock.UtcNow;
        var healthWorker = user.IsHealthWorker;
        var visit = new Visit
        {
            Id = request.Id!,
            PatientId = patientId,
            ProviderUserId = user.Id,
            ProviderName = healthWorker ? user.Name : SelfReported,
            FacilityId = healthWorker ? user.FacilityId : null,
            FacilityName = healthWorker ? user.FacilityName : null,
            VisitAt = request.VisitAt!.Value,
            ChiefComplaintCode = request.ChiefComplaintCode!,
            Vitals = request.Vitals ?? new VitalsDto(),
            DiagnosisCodes = (request.DiagnosisCodes ?? []).Distinct().ToList(),
            Notes = Blank(request.Notes),
            Advice = Blank(request.Advice),
            FollowUpAt = request.FollowUpAt,
            Referral = request.Referral is null
                ? null
                : new ReferralDto(Blank(request.Referral.FacilityId), request.Referral.FacilityName!.Trim(),
                    request.Referral.Reason!.Trim(), WireEnum.Parse<ReferralUrgency>(request.Referral.Urgency!)),
            Prescriptions = (request.Prescriptions ?? []).Select(p => new PrescriptionDto(
                p.Id!, p.DrugCode!,
                string.IsNullOrWhiteSpace(p.DrugName) ? drugLabels[p.DrugCode!].LabelEn : p.DrugName.Trim(),
                p.Dose!.Trim(), WireEnum.Parse<PrescriptionFrequency>(p.Frequency!), p.DurationDays!.Value,
                Blank(p.InstructionsNp))).ToList(),
            SupersedesId = request.SupersedesId,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        };

        db.Visits.Add(visit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return Idempotent(await db.Visits.AsNoTracking().FirstAsync(v => v.Id == request.Id, ct), patientId);
        }

        await audit.WriteAsync(patientId, AuditAction.VisitAdded, ct);
        if (visit.SupersedesId is { } superseded)
            await events.PublishAsync(new VisitSuperseded(superseded, patientId, visit.Id), ct);
        if (visit.FollowUpAt is { } followUp)
            await events.PublishAsync(new FollowUpScheduled(visit.Id, patientId, followUp, user.Id), ct);
        return visit.ToDto();
    }

    public async Task<IReadOnlyList<VisitDto>> ListAsync(string patientId, int limit, CancellationToken ct)
    {
        await access.RequireReadAsync(patientId, ct);
        var visits = await db.Visits.AsNoTracking()
            .Where(v => v.PatientId == patientId && !v.Deleted)
            .OrderByDescending(v => v.VisitAt).ThenByDescending(v => v.Id)
            .Take(limit)
            .ToListAsync(ct);
        return visits.Select(v => v.ToDto()).ToList();
    }

    private static VisitDto Idempotent(Visit existing, string patientId) =>
        existing.PatientId == patientId
            ? existing.ToDto()
            : throw AppException.Validation("id", "This id is already used by a visit of another patient");

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
