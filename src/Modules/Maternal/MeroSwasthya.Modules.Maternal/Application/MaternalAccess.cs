using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Security;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>
/// Access and audit for every Maternal endpoint, exactly as Clinical: <see cref="IPatientAccess"/>
/// decides (owner, or a provider/FCHV holding an active grant → 404 / 403 FORBIDDEN / 403 GRANT_EXPIRED),
/// a read by someone other than the owner is reported to the <see cref="IPatientReadObserver"/>s
/// (Audit writes a throttled <c>record_viewed</c>), and a write is audited as <c>contact_recorded</c> —
/// the only A.2 audit action for maternal care, so it covers registering a pregnancy and recording a
/// delivery as well as the ANC contact A.4 names (the app's enum accepts nothing else).
/// </summary>
internal sealed class MaternalAccess(
    IPatientAccess access,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IEnumerable<IPatientReadObserver> readObservers)
{
    public async Task<PatientDto> ReadAsync(string patientId, CancellationToken ct)
    {
        var patient = await access.RequireReadAsync(patientId, ct);
        var reader = await currentUser.GetAsync(ct);
        if (patient.OwnerUserId != reader.Id)
            foreach (var observer in readObservers)
                await observer.OnRecordViewedAsync(patient, reader, ct);
        return patient;
    }

    /// <summary>Owner, or a health worker with an active append grant. FCHVs may record maternal care (A.2 roles).</summary>
    public Task<PatientDto> AppendAsync(string patientId, CancellationToken ct) => access.RequireAppendAsync(patientId, ct);

    /// <summary>Called after the module's own SaveChanges succeeded, so a failed write never leaves an audit line.</summary>
    public Task WroteAsync(string patientId, CancellationToken ct) => audit.WriteAsync(patientId, AuditAction.ContactRecorded, ct);
}
