using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Clinical.Domain;

/// <summary>A.2 Prescription.frequency.</summary>
public enum PrescriptionFrequency
{
    [WireName("OD")] Od,
    [WireName("BD")] Bd,
    [WireName("TDS")] Tds,
    [WireName("QID")] Qid,
    [WireName("SOS")] Sos,
    [WireName("HS")] Hs,
}

/// <summary>A.2 Visit.referral.urgency.</summary>
public enum ReferralUrgency
{
    [WireName("routine")] Routine,
    [WireName("urgent")] Urgent,
}

/// <summary>A.2 Document.type.</summary>
public enum DocumentType
{
    [WireName("prescription")] Prescription,
    [WireName("lab")] Lab,
    [WireName("discharge")] Discharge,
    [WireName("referral")] Referral,
    [WireName("other")] Other,
}

/// <summary>A.2 Document.status.</summary>
public enum DocumentStatus
{
    [WireName("pending_upload")] PendingUpload,
    [WireName("uploaded")] Uploaded,
}

/// <summary>A.2 Document.aiSummaryStatus.</summary>
public enum AiSummaryStatus
{
    [WireName("none")] None,
    [WireName("queued")] Queued,
    [WireName("done")] Done,
    [WireName("failed")] Failed,
}

/// <summary>A.2 Visit. Append-only: a correction is a new Visit with <see cref="SupersedesId"/>.</summary>
internal sealed class Visit
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public required string ProviderUserId { get; init; }
    public required string ProviderName { get; init; }
    public string? FacilityId { get; init; }
    public string? FacilityName { get; init; }
    public DateTime VisitAt { get; init; }
    public required string ChiefComplaintCode { get; init; }
    public VitalsDto Vitals { get; init; } = new();
    public List<string> DiagnosisCodes { get; init; } = [];
    public string? Notes { get; init; }
    public string? Advice { get; init; }
    public DateOnly? FollowUpAt { get; init; }
    public ReferralDto? Referral { get; init; }
    public List<PrescriptionDto> Prescriptions { get; init; } = [];
    public string? SupersedesId { get; init; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A.2 Document metadata. The bytes live in object storage (MinIO) or, in development, on local disk.</summary>
internal sealed class Document
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public required string UploadedByUserId { get; init; }
    public DocumentType Type { get; init; }
    public required string Title { get; init; }
    public DateOnly TakenAt { get; init; }
    public DocumentStatus Status { get; set; }
    public required string ContentType { get; init; }
    public long SizeBytes { get; init; }

    /// <summary><c>patients/{patientId}/{documentId}.jpg|png</c>.</summary>
    public required string ObjectKey { get; init; }

    /// <summary>Where the bytes are: "s3", "local", or null while nothing has been stored.</summary>
    public string? Storage { get; set; }
    public string? AiSummary { get; set; }
    public AiSummaryStatus AiSummaryStatus { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; init; }
}
