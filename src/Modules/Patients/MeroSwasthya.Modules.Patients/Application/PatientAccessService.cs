using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Modules.Patients.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Patients.Application;

internal static class PatientMapping
{
    public static PatientDto ToDto(this Patient p) => new(
        p.Id, p.OwnerUserId, p.Name, p.Sex, p.Dob, p.BloodGroup, p.Ward, p.Municipality,
        p.Allergies.ToList(), p.ChronicConditions.ToList(), p.EmergencyContactPhone, p.Version, p.UpdatedAt, p.Deleted);
}

/// <summary>
/// The single place that decides who may see a patient: the owner always; a provider/FCHV while a
/// grant is active (grants come from <see cref="IPatientGrantSource"/>, implemented by the Grants
/// module — until it exists no grant source is registered and only owners have access).
/// </summary>
internal sealed class PatientAccessService(
    PatientsDbContext db,
    ICurrentUser currentUser,
    IEnumerable<IPatientGrantSource> grantSources) : IPatientAccess
{
    public Task<PatientDto> RequireReadAsync(string patientId, CancellationToken ct = default) =>
        RequireAsync(patientId, PatientAccessLevel.Read, ct);

    public Task<PatientDto> RequireAppendAsync(string patientId, CancellationToken ct = default) =>
        RequireAsync(patientId, PatientAccessLevel.Append, ct);

    public Task<PatientDto> RequireOwnerAsync(string patientId, CancellationToken ct = default) =>
        RequireAsync(patientId, PatientAccessLevel.Owner, ct);

    public async Task<IReadOnlyList<string>> VisiblePatientIdsAsync(CancellationToken ct = default)
    {
        var user = await currentUser.GetAsync(ct);
        var ids = await db.Patients.AsNoTracking()
            .Where(p => p.OwnerUserId == user.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (user.IsHealthWorker)
        {
            foreach (var source in grantSources)
                ids.AddRange(await source.ActivePatientIdsAsync(user, ct));
        }
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    internal async Task<(Patient Patient, PatientAccessLevel Level)> LoadWithAccessAsync(
        string patientId, PatientAccessLevel required, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null || patient.Deleted) throw AppException.NotFound("Patient");

        var (level, expired) = await LevelAsync(patient, user, ct);
        if (level < required)
        {
            // A.3: an access window that ended is GRANT_EXPIRED; no grant at all is FORBIDDEN.
            if (level == PatientAccessLevel.None && expired)
                throw new AppException(ErrorCode.GrantExpired, "Access to this patient has expired");
            throw AppException.Forbidden(required == PatientAccessLevel.Owner
                ? "Only the owner of this record can do this"
                : "No active grant for this patient");
        }
        return (patient, level);
    }

    private async Task<PatientDto> RequireAsync(string patientId, PatientAccessLevel required, CancellationToken ct) =>
        (await LoadWithAccessAsync(patientId, required, ct)).Patient.ToDto();

    private async Task<(PatientAccessLevel Level, bool Expired)> LevelAsync(
        Patient patient, CurrentUserInfo user, CancellationToken ct)
    {
        if (patient.OwnerUserId == user.Id) return (PatientAccessLevel.Owner, false);
        if (!user.IsHealthWorker) return (PatientAccessLevel.None, false);

        var best = PatientAccessLevel.None;
        var expired = false;
        foreach (var source in grantSources)
        {
            var decision = await source.EvaluateAsync(patient.Id, user, ct);
            if (decision.Level > best) best = decision.Level;
            expired |= decision.Expired;
        }
        return (best, expired);
    }
}
