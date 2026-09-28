using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Audit.Contracts;

/// <summary>A.2 AuditEntry.action.</summary>
public enum AuditAction
{
    [WireName("grant_created")] GrantCreated,
    [WireName("grant_redeemed")] GrantRedeemed,
    [WireName("record_viewed")] RecordViewed,
    [WireName("visit_added")] VisitAdded,
    [WireName("contact_recorded")] ContactRecorded,
    [WireName("document_added")] DocumentAdded,
    [WireName("grant_revoked")] GrantRevoked,
}

/// <summary>
/// Appends to the patient's access log. The actor (id, name, facility) is the current user. Used by
/// Grants (created / redeemed / revoked), Clinical (visit_added, document_added) and, later,
/// Maternal (contact_recorded). Written after the producing module has saved its own change.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(string patientId, AuditAction action, CancellationToken ct = default);
}

/// <summary>A.2 AuditEntry.</summary>
public sealed record AuditEntryDto(
    string Id,
    string PatientId,
    string ActorUserId,
    string ActorName,
    string? ActorFacilityName,
    AuditAction Action,
    DateTime At);
