using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Maternal.Domain;

/// <summary>A.2 AncContact.referral.urgency (same shape as Visit.referral).</summary>
public enum ReferralUrgency
{
    [WireName("routine")] Routine,
    [WireName("urgent")] Urgent,
}

/// <summary>A.2 Pregnancy. Syncable: client id, server version/updatedAt. Computed fields (gestationalAgeDays, nextContact) are not stored.</summary>
internal sealed class Pregnancy
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public DateOnly? Lmp { get; init; }
    public DateOnly Edd { get; init; }
    public int Gravida { get; init; }
    public int Para { get; init; }
    public List<string> RiskFactors { get; set; } = [];
    public RiskLevel RiskLevel { get; set; }
    public PregnancyStatus Status { get; set; }
    public BirthPlanDto? BirthPlan { get; set; }
    public required string RegisteredByUserId { get; init; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A.2 AncContact — one of the eight scheduled contacts, created empty at registration with the
/// deterministic id <c>Ids.AncContactId(pregnancyId, contactNo)</c> (A.8.15) and filled when the
/// contact happens. <see cref="PatientId"/> is denormalised for timeline and access queries.
/// </summary>
internal sealed class AncContact
{
    public required string Id { get; init; }
    public required string PregnancyId { get; init; }
    public required string PatientId { get; init; }
    public int ContactNo { get; init; }
    public int WeekTarget { get; init; }
    public DateOnly DueAt { get; set; }
    public DateTime? DoneAt { get; set; }
    public string? ProviderUserId { get; set; }
    public FindingsDto? Findings { get; set; }
    public List<string> DangerSigns { get; set; } = [];
    public TriageLevel? TriageLevel { get; set; }
    public List<string> TriageReasons { get; set; } = [];
    public ReferralDto? Referral { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; init; }
}
