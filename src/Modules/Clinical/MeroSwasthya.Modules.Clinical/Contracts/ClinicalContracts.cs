using System.Text.Json.Serialization;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Shared.Events;

namespace MeroSwasthya.Modules.Clinical.Contracts;

/// <summary>
/// A.2 Visit.vitals — "{ bpSys?, bpDia?, pulse?, tempC?, weightKg?, spo2? } — all optional": keys that
/// were not measured are omitted, exactly as in the Part A example.
/// </summary>
public sealed record VitalsDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BpSys { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BpDia { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Pulse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? TempC { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WeightKg { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Spo2 { get; init; }

    [JsonIgnore]
    public bool IsEmpty => BpSys is null && BpDia is null && Pulse is null && TempC is null && WeightKg is null && Spo2 is null;
}

/// <summary>A.2 Visit.referral — <c>{ facilityId?, facilityName, reason, urgency }</c>; also AncContact.referral.</summary>
public sealed record ReferralDto(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FacilityId,
    string FacilityName,
    string Reason,
    ReferralUrgency Urgency);

/// <summary>A.2 Prescription.</summary>
public sealed record PrescriptionDto(
    string Id,
    string DrugCode,
    string DrugName,
    string Dose,
    PrescriptionFrequency Frequency,
    int DurationDays,
    string? InstructionsNp);

/// <summary>A.2 Visit.</summary>
public sealed record VisitDto(
    string Id,
    string PatientId,
    string ProviderUserId,
    string ProviderName,
    string? FacilityId,
    string? FacilityName,
    DateTime VisitAt,
    string ChiefComplaintCode,
    VitalsDto Vitals,
    IReadOnlyList<string> DiagnosisCodes,
    string? Notes,
    string? Advice,
    DateOnly? FollowUpAt,
    ReferralDto? Referral,
    IReadOnlyList<PrescriptionDto> Prescriptions,
    string? SupersedesId,
    int Version,
    DateTime UpdatedAt,
    bool Deleted);

/// <summary>A.2 Document. <c>downloadUrl</c> is a fresh 1 h URL when status = uploaded, else null.</summary>
public sealed record DocumentDto(
    string Id,
    string PatientId,
    string UploadedByUserId,
    DocumentType Type,
    string Title,
    DateOnly TakenAt,
    DocumentStatus Status,
    string? DownloadUrl,
    string? AiSummary,
    AiSummaryStatus AiSummaryStatus,
    int Version,
    DateTime UpdatedAt,
    bool Deleted);

/// <summary>
/// Raised after a visit with <c>followUpAt</c> is stored. The Reminders module subscribes
/// (A.5 reminders.follow_up: "Send 1 day before Visit.followUpAt to patient phone").
/// </summary>
public sealed record FollowUpScheduled(string VisitId, string PatientId, DateOnly FollowUpAt, string ProviderUserId) : IDomainEvent;

/// <summary>
/// Raised after a correction (a visit with <c>supersedesId</c>) is stored. Reminders cancels the
/// pending follow-up of the visit that was replaced; the correction schedules its own.
/// </summary>
public sealed record VisitSuperseded(string SupersededVisitId, string PatientId, string ByVisitId) : IDomainEvent;
