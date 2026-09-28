using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Modules.Patients.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeroSwasthya.Modules.Patients.Application;

internal sealed record PatientResponse(PatientDto Patient);

internal sealed record PatientDetailResponse(PatientDto Patient, PatientSummaryDto Summary);

/// <summary>A.4 "Patients (family profiles)".</summary>
internal sealed class PatientService(
    PatientsDbContext db,
    ICurrentUser currentUser,
    PatientAccessService access,
    IPatientSummaryService summaries,
    IPatientTimelineService timeline,
    IEnumerable<IPatientGrantSource> grantSources,
    IClock clock)
{
    /// <summary>Owned profiles for everyone; health workers additionally see patients they hold an active grant for.</summary>
    public async Task<IReadOnlyList<PatientDto>> ListAsync(CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (user.IsHealthWorker)
            foreach (var source in grantSources)
                granted.UnionWith(await source.ActivePatientIdsAsync(user, ct));

        var grantedIds = granted.ToList();
        var rows = await db.Patients.AsNoTracking()
            .Where(p => !p.Deleted && (p.OwnerUserId == user.Id || grantedIds.Contains(p.Id)))
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);
        return rows.Select(p => p.ToDto()).ToList();
    }

    /// <summary>Client-generated id; creating the same id twice returns the existing row (A.1).</summary>
    public async Task<PatientDto> CreateAsync(CreatePatientRequest request, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var existing = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (existing is not null) return Idempotent(existing, user);

        var now = clock.UtcNow;
        var patient = new Patient
        {
            Id = request.Id!,
            OwnerUserId = user.Id,
            Name = request.Name!.Trim(),
            Sex = WireEnum.Parse<Sex>(request.Sex!),
            Dob = request.Dob!.Value,
            BloodGroup = request.BloodGroup,
            Ward = request.Ward,
            Municipality = request.Municipality?.Trim(),
            Allergies = Clean(request.Allergies),
            ChronicConditions = Clean(request.ChronicConditions),
            EmergencyContactPhone = request.EmergencyContactPhone,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        };
        db.Patients.Add(patient);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two retries of the same outbox op raced; the other one won. Answer as if we were second.
            db.ChangeTracker.Clear();
            existing = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == request.Id, ct);
            return Idempotent(existing, user);
        }
        return patient.ToDto();
    }

    public async Task<PatientDetailResponse> GetAsync(string id, CancellationToken ct)
    {
        var (patient, _) = await access.LoadWithAccessAsync(id, PatientAccessLevel.Read, ct);
        // TODO(Grants): write AuditEntry record_viewed when a health worker (not the owner) reads the record.
        var dto = patient.ToDto();
        return new PatientDetailResponse(dto, await summaries.BuildAsync(dto, ct));
    }

    /// <summary>Owner only. <c>version</c> must equal the stored version, else 409 VERSION_CONFLICT with details.current.</summary>
    public async Task<PatientDto> PatchAsync(string id, PatchPatientRequest request, CancellationToken ct)
    {
        await access.LoadWithAccessAsync(id, PatientAccessLevel.Owner, ct);
        var patient = await db.Patients.FirstAsync(p => p.Id == id, ct);
        if (patient.Version != request.Version)
            throw AppException.VersionConflict(patient.ToDto());

        if (request.Name.HasValue) patient.Name = request.Name.Value!.Trim();
        if (request.Sex.HasValue) patient.Sex = WireEnum.Parse<Sex>(request.Sex.Value!);
        if (request.Dob.HasValue) patient.Dob = request.Dob.Value!.Value;
        if (request.BloodGroup.HasValue) patient.BloodGroup = request.BloodGroup.Value;
        if (request.Ward.HasValue) patient.Ward = request.Ward.Value;
        if (request.Municipality.HasValue) patient.Municipality = request.Municipality.Value?.Trim();
        if (request.Allergies.HasValue) patient.Allergies = Clean(request.Allergies.Value);
        if (request.ChronicConditions.HasValue) patient.ChronicConditions = Clean(request.ChronicConditions.Value);
        if (request.EmergencyContactPhone.HasValue) patient.EmergencyContactPhone = request.EmergencyContactPhone.Value;
        // TODO(Maternal): a dob correction must regenerate the immunisation schedule's dueAt (addendum §1).

        patient.Version++;
        patient.UpdatedAt = clock.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else's write landed between our read and our UPDATE … WHERE version = n.
            db.ChangeTracker.Clear();
            var current = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == id, ct);
            throw AppException.VersionConflict(current.ToDto());
        }
        return patient.ToDto();
    }

    public async Task<TimelinePageDto> TimelineAsync(string id, DateTime? before, int limit, CancellationToken ct)
    {
        await access.LoadWithAccessAsync(id, PatientAccessLevel.Read, ct);
        return await timeline.GetPageAsync(id, before, limit, ct);
    }

    private static PatientDto Idempotent(Patient existing, CurrentUserInfo user)
    {
        if (existing.OwnerUserId != user.Id)
            throw AppException.Forbidden("This id belongs to a record owned by another account");
        return existing.ToDto();
    }

    private static List<string> Clean(IEnumerable<string>? items) =>
        items?.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToList() ?? [];
}
