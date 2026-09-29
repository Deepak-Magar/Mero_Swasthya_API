using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Reminders.Domain;

/// <summary>A.2 Reminder.kind.</summary>
public enum ReminderKind
{
    [WireName("anc_due")] AncDue,
    [WireName("anc_missed")] AncMissed,
    [WireName("follow_up")] FollowUp,
    [WireName("medicine")] Medicine,
}

/// <summary>A.2 Reminder.channel.</summary>
public enum ReminderChannel
{
    [WireName("sms")] Sms,
    [WireName("push")] Push,
}

/// <summary>A.2 Reminder.recipientRole.</summary>
public enum RecipientRole
{
    [WireName("patient")] Patient,
    [WireName("family")] Family,
}

/// <summary>A.2 Reminder.status. (A cancelled reminder keeps its status and leaves the list — the app's enum knows no other value.)</summary>
public enum ReminderStatus
{
    [WireName("pending")] Pending,
    [WireName("sent")] Sent,
    [WireName("failed")] Failed,
}

/// <summary>
/// A.2 Reminder — a scheduled SMS/push. Server-owned (not syncable). <see cref="SourceKey"/> names
/// what it is about (<c>anc_contact:{id}:due</c>, <c>anc_contact:{id}:missed:3</c>, <c>visit:{id}</c>)
/// so re-scheduling never duplicates a message and recording a contact can cancel its pending ones.
/// </summary>
internal sealed class Reminder
{
    public required string Id { get; init; }
    public required string PatientId { get; init; }
    public string? PregnancyId { get; init; }
    public ReminderKind Kind { get; init; }
    public DateTime DueAt { get; set; }
    public ReminderChannel Channel { get; init; }
    public required string RecipientPhone { get; init; }
    public RecipientRole RecipientRole { get; init; }
    public required string MessageNp { get; set; }
    public required string MessageEn { get; set; }
    public ReminderStatus Status { get; set; }
    public DateTime? SentAt { get; set; }

    public required string SourceKey { get; init; }
    public DateTime? CancelledAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}
