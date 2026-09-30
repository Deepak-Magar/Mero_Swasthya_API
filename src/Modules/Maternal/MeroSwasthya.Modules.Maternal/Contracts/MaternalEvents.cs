using MeroSwasthya.Shared.Events;

namespace MeroSwasthya.Modules.Maternal.Contracts;

/// <summary>
/// Raised after a pregnancy and its eight ANC contacts are stored. The Reminders module subscribes
/// (A.4: "creates 8 AncContacts with deterministic ids and the Reminders"). Ids only — the subscriber
/// reads the schedule through <see cref="IPregnancyDirectory"/>, so no clinical data reaches the event log.
/// </summary>
public sealed record PregnancyRegistered(string PregnancyId, string PatientId) : IDomainEvent;

/// <summary>
/// Raised after an ANC contact is recorded (or re-recorded). Reminders cancels what is still pending
/// for that contact (A.4: "Cancels pending anc_missed reminder for this contact").
/// </summary>
public sealed record AncContactRecorded(string PregnancyId, string PatientId, string ContactId, int ContactNo) : IDomainEvent;

/// <summary>Raised after a pregnancy stops being active (delivery recorded, or status set to <c>ended</c>): nothing more is due.</summary>
public sealed record PregnancyClosed(string PregnancyId, string PatientId) : IDomainEvent;
