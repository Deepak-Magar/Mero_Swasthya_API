using MeroSwasthya.Modules.Reminders.Domain;

namespace MeroSwasthya.Modules.Reminders.Contracts;

/// <summary>A.2 Reminder.</summary>
public sealed record ReminderDto(
    string Id,
    string PatientId,
    string? PregnancyId,
    ReminderKind Kind,
    DateTime DueAt,
    ReminderChannel Channel,
    string RecipientPhone,
    RecipientRole RecipientRole,
    string MessageNp,
    string MessageEn,
    ReminderStatus Status,
    DateTime? SentAt);
