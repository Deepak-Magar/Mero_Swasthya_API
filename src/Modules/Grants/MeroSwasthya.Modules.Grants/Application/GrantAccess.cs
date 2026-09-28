using MeroSwasthya.Modules.Grants.Contracts;
using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Modules.Grants.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Grants.Application;

/// <summary>
/// Plugs grants into the Patients module's access policy (GET /patients, GET /patients/:id, timeline,
/// and every <c>IPatientAccess</c> check other modules make).
/// </summary>
internal sealed class PatientGrantSource(GrantsDbContext db, IClock clock) : IPatientGrantSource
{
    public async Task<GrantAccessDecision> EvaluateAsync(string patientId, CurrentUserInfo user, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var redeemed = await db.Grants.AsNoTracking()
            .Where(g => g.PatientId == patientId && g.RedeemedByUserId == user.Id)
            .Select(g => new { g.Scope, g.RevokedAt, g.AccessUntil })
            .ToListAsync(ct);

        var active = redeemed.Where(g => g.RevokedAt == null && g.AccessUntil > now).ToList();
        if (active.Count > 0)
            return new GrantAccessDecision(
                active.Any(g => g.Scope == GrantScope.Append) ? PatientAccessLevel.Append : PatientAccessLevel.Read, false);

        // A.3 / A.6 #16: a 24 h window that ran out is GRANT_EXPIRED; a revoked grant is simply no access (FORBIDDEN).
        var windowEnded = redeemed.Any(g => g.RevokedAt == null && g.AccessUntil <= now);
        return new GrantAccessDecision(PatientAccessLevel.None, windowEnded);
    }

    public async Task<IReadOnlyCollection<string>> ActivePatientIdsAsync(CurrentUserInfo user, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return await db.Grants.AsNoTracking()
            .Where(g => g.RedeemedByUserId == user.Id && g.RevokedAt == null && g.AccessUntil > now)
            .Select(g => g.PatientId)
            .Distinct()
            .ToListAsync(ct);
    }
}

internal sealed class GrantAuthorization(GrantsDbContext db, IPatientDirectory patients, IClock clock) : IGrantAuthorization
{
    public Task<bool> CanReadPatientAsync(string userId, string patientId, CancellationToken ct = default) =>
        CanAsync(userId, patientId, appendRequired: false, ct);

    public Task<bool> CanAppendPatientAsync(string userId, string patientId, CancellationToken ct = default) =>
        CanAsync(userId, patientId, appendRequired: true, ct);

    private async Task<bool> CanAsync(string userId, string patientId, bool appendRequired, CancellationToken ct)
    {
        var patient = await patients.FindAsync(patientId, ct);
        if (patient is null || patient.Deleted) return false;
        if (patient.OwnerUserId == userId) return true;

        var now = clock.UtcNow;
        return await db.Grants.AsNoTracking().AnyAsync(g =>
            g.PatientId == patientId && g.RedeemedByUserId == userId && g.RevokedAt == null && g.AccessUntil > now &&
            (!appendRequired || g.Scope == GrantScope.Append), ct);
    }
}
