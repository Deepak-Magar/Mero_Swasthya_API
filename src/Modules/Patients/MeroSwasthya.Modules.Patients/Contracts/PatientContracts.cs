using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;

namespace MeroSwasthya.Modules.Patients.Contracts;

/// <summary>A.2 Patient — the one serializer for the entity, used by every module that returns a patient.</summary>
public sealed record PatientDto(
    string Id,
    string OwnerUserId,
    string Name,
    Sex Sex,
    DateOnly Dob,
    string? BloodGroup,
    int? Ward,
    string? Municipality,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> ChronicConditions,
    string? EmergencyContactPhone,
    int Version,
    DateTime UpdatedAt,
    bool Deleted);

/// <summary>What a caller may do with one patient record.</summary>
public enum PatientAccessLevel
{
    None,
    Read,
    Append,
    Owner,
}

/// <summary>
/// Access checks for any module that touches patient data (Clinical, Maternal, Sync, …). Uses the
/// current user; throws 404 NOT_FOUND (missing / soft-deleted), 403 FORBIDDEN or 403 GRANT_EXPIRED.
/// </summary>
public interface IPatientAccess
{
    Task<PatientDto> RequireReadAsync(string patientId, CancellationToken ct = default);

    /// <summary>Owner, or a health worker with an active <c>append</c> grant.</summary>
    Task<PatientDto> RequireAppendAsync(string patientId, CancellationToken ct = default);

    Task<PatientDto> RequireOwnerAsync(string patientId, CancellationToken ct = default);

    /// <summary>Patients the caller may see: owned, plus active grants (A.4 GET /sync/pull visibility).</summary>
    Task<IReadOnlyList<string>> VisiblePatientIdsAsync(CancellationToken ct = default);
}

/// <summary>Read-only patient lookups that bypass access checks — for server-side jobs (Reminders) only.</summary>
public interface IPatientDirectory
{
    Task<PatientDto?> FindAsync(string patientId, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------------------------
// Extension points. Patients owns the shapes; later modules contribute through these interfaces.
// ---------------------------------------------------------------------------------------------

/// <summary>A grant module's verdict on a health worker's access to one patient.</summary>
public sealed record GrantAccessDecision(PatientAccessLevel Level, bool Expired)
{
    public static readonly GrantAccessDecision None = new(PatientAccessLevel.None, false);
}

/// <summary>Implemented by the Grants module: which patients a health worker currently has a grant for.</summary>
public interface IPatientGrantSource
{
    Task<GrantAccessDecision> EvaluateAsync(string patientId, CurrentUserInfo user, CancellationToken ct = default);

    Task<IReadOnlyCollection<string>> ActivePatientIdsAsync(CurrentUserInfo user, CancellationToken ct = default);
}

/// <summary>A.4 GET /patients/:id summary.activeProblems[].</summary>
public sealed record ActiveProblemDto(string Code, string LabelEn, string LabelNp, DateOnly? Since);

/// <summary>A.4 summary.lastVitals.</summary>
public sealed record LastVitalsDto(int? BpSys, int? BpDia, double? WeightKg, DateTime At);

/// <summary>
/// A.4 GET /patients/:id <c>summary</c> (also inside the grant redeem bundle). An empty summary is
/// <see cref="Empty"/>: present with empty lists and nulls, never null itself (addendum §4).
/// </summary>
public sealed record PatientSummaryDto(
    IReadOnlyList<ActiveProblemDto> ActiveProblems,
    IReadOnlyList<object> CurrentMedicines,
    IReadOnlyList<string> Allergies,
    LastVitalsDto? LastVitals,
    object? ActivePregnancy,
    DateTime? LastVisitAt,
    int VisitCount)
{
    public static readonly PatientSummaryDto Empty = new([], [], [], null, null, null, 0);
}

/// <summary>Mutable while contributors run; frozen into <see cref="PatientSummaryDto"/>.</summary>
public sealed class PatientSummaryBuilder(PatientDto patient)
{
    public PatientDto Patient { get; } = patient;
    public List<ActiveProblemDto> ActiveProblems { get; } = [];
    public List<object> CurrentMedicines { get; } = [];
    public List<string> Allergies { get; } = [.. patient.Allergies];
    public LastVitalsDto? LastVitals { get; set; }
    public object? ActivePregnancy { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public int VisitCount { get; set; }

    public PatientSummaryDto Build() =>
        new(ActiveProblems.ToList(), CurrentMedicines.ToList(), Allergies.ToList(), LastVitals, ActivePregnancy, LastVisitAt, VisitCount);
}

/// <summary>
/// Clinical adds currentMedicines / lastVitals / lastVisitAt / visitCount (and dated activeProblems);
/// Maternal adds activePregnancy. Run in <see cref="Order"/> after the Patients base (order 0).
/// </summary>
public interface IPatientSummaryContributor
{
    int Order { get; }

    Task ContributeAsync(PatientSummaryBuilder summary, CancellationToken ct = default);
}

/// <summary>A.2 TimelineItem.kind (+ addendum §3 immunisation, growth).</summary>
public enum TimelineKind
{
    [WireName("visit")] Visit,
    [WireName("document")] Document,
    [WireName("pregnancy_registered")] PregnancyRegistered,
    [WireName("anc_contact")] AncContact,
    [WireName("delivery")] Delivery,
    [WireName("immunisation")] Immunisation,
    [WireName("growth")] Growth,
}

/// <summary>A.2 TimelineItem.badge.</summary>
public enum TimelineBadge
{
    [WireName("green")] Green,
    [WireName("amber")] Amber,
    [WireName("red")] Red,
}

/// <summary>A.2 TimelineItem. <see cref="Payload"/> is the full underlying entity DTO.</summary>
public sealed record TimelineItemDto(
    TimelineKind Kind,
    DateTime At,
    string Title,
    string? Subtitle,
    TimelineBadge? Badge,
    string RefId,
    object Payload);

/// <summary>A.4 GET /patients/:id/timeline data.</summary>
public sealed record TimelinePageDto(IReadOnlyList<TimelineItemDto> Items, DateTime? NextBefore);

/// <summary>
/// Each module that owns timeline entities returns its newest items strictly before
/// <c>before</c> (null = now), newest first, at most <c>limit</c>. The Patients module merges them.
/// </summary>
public interface ITimelineContributor
{
    Task<IReadOnlyList<TimelineItemDto>> GetAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default);
}

/// <summary>Builders used by the Patients endpoints and, later, by the Grants redeem bundle.</summary>
public interface IPatientSummaryService
{
    Task<PatientSummaryDto> BuildAsync(PatientDto patient, CancellationToken ct = default);
}

public interface IPatientTimelineService
{
    Task<TimelinePageDto> GetPageAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default);
}

/// <summary>
/// Notified after a caller who is not the owner (a health worker with a grant) reads a patient
/// record via GET /patients/:id. The Audit module writes <c>record_viewed</c> from it.
/// </summary>
public interface IPatientReadObserver
{
    Task OnRecordViewedAsync(PatientDto patient, CurrentUserInfo reader, CancellationToken ct = default);
}

/// <summary>The active pregnancy and its ANC contacts, as their A.2 DTOs.</summary>
public sealed record ActivePregnancySnapshot(object Pregnancy, IReadOnlyList<object> AncContacts);

/// <summary>
/// Implemented by the Maternal module; used by the grant redeem bundle (pregnancy, ancContacts).
/// With no implementation registered the bundle carries <c>pregnancy: null, ancContacts: []</c>.
/// </summary>
public interface IActivePregnancySource
{
    Task<ActivePregnancySnapshot?> GetAsync(string patientId, CancellationToken ct = default);
}
