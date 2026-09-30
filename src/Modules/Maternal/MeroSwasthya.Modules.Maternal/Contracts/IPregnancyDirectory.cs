namespace MeroSwasthya.Modules.Maternal.Contracts;

/// <summary>A pregnancy with its contacts (by contactNo, soft-deleted ones left out), as their A.2 DTOs.</summary>
public sealed record PregnancySnapshot(PregnancyDto Pregnancy, IReadOnlyList<AncContactDto> Contacts);

/// <summary>Read-only pregnancy lookups that bypass access checks — for server-side jobs (Reminders) only.</summary>
public interface IPregnancyDirectory
{
    Task<PregnancySnapshot?> FindAsync(string pregnancyId, CancellationToken ct = default);
}
