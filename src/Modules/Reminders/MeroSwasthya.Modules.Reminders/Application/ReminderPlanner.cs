using MeroSwasthya.Modules.Reminders.Domain;

namespace MeroSwasthya.Modules.Reminders.Application;

/// <summary>One message to schedule, before it is fanned out to its recipients.</summary>
internal sealed record PlannedReminder(ReminderKind Kind, string SourceKey, DateTime DueAt, string MessageNp, string MessageEn);

/// <summary>
/// A.5 <c>reminders</c>, as the app's <c>reminders_preview.dart</c> computes them: every message goes out
/// at 09:00 Asia/Kathmandu (03:15 UTC) — <c>anc_due</c> the day before the contact, <c>anc_missed</c>
/// 3 and 7 days after it, <c>follow_up</c> the day before <c>Visit.followUpAt</c>.
/// </summary>
internal static class ReminderPlanner
{
    /// <summary>Asia/Kathmandu is UTC+05:45 all year.</summary>
    public static readonly TimeSpan KathmanduOffset = new(5, 45, 0);

    public static readonly TimeOnly SendTime = new(9, 0);

    /// <summary>"Send 3 days after dueAt if doneAt is still null; repeat once after 7 days."</summary>
    public static readonly int[] MissedAfterDays = [3, 7];

    /// <summary>09:00 in Kathmandu on <paramref name="day"/>, as a UTC instant.</summary>
    public static DateTime SendAt(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(SendTime) - KathmanduOffset, DateTimeKind.Utc);

    /// <summary>Every reminder about one ANC contact has a source key starting with this.</summary>
    public static string ContactPrefix(string contactId) => $"anc_contact:{contactId}:";

    public static string VisitKey(string visitId) => $"visit:{visitId}";

    /// <summary>The three messages about one contact that has not happened yet: due, missed +3 d, missed +7 d.</summary>
    public static IReadOnlyList<PlannedReminder> ForContact(string patientName, string contactId, int contactNo, DateOnly dueAt, string? facilityName)
    {
        var prefix = ContactPrefix(contactId);
        var due = ReminderTexts.AncDue(patientName, contactNo, dueAt, facilityName);
        var missed = ReminderTexts.AncMissed(patientName, contactNo, dueAt, facilityName);

        var planned = new List<PlannedReminder>
        {
            new(ReminderKind.AncDue, $"{prefix}due", SendAt(dueAt.AddDays(-1)), due.Np, due.En),
        };
        foreach (var days in MissedAfterDays)
            planned.Add(new PlannedReminder(ReminderKind.AncMissed, $"{prefix}missed:{days}", SendAt(dueAt.AddDays(days)), missed.Np, missed.En));
        return planned;
    }

    public static PlannedReminder ForFollowUp(string patientName, string visitId, DateOnly followUpAt, string? facilityName)
    {
        var text = ReminderTexts.FollowUp(patientName, followUpAt, facilityName);
        return new PlannedReminder(ReminderKind.FollowUp, VisitKey(visitId), SendAt(followUpAt.AddDays(-1)), text.Np, text.En);
    }
}

/// <summary>
/// The message texts. <c>anc_due</c> is the A.2 Reminder example word for word; Nepali text carries the
/// BS date and Nepali digits ("४ औं"), English the AD date. The facility clause is dropped when the
/// record has no facility (a pregnancy or visit the owner entered herself).
/// </summary>
internal static class ReminderTexts
{
    public static (string Np, string En) AncDue(string patientName, int contactNo, DateOnly dueAt, string? facilityName)
    {
        var no = BsCalendar.NepaliDigits(contactNo.ToString());
        return facilityName is null
            ? ($"{Np(patientName, "को")} {no} औं गर्भ जाँच {BsCalendar.Format(dueAt)} मा छ।",
                $"{patientName}'s ANC contact {contactNo} is due on {dueAt:yyyy-MM-dd}.")
            : ($"{Np(patientName, "को")} {no} औं गर्भ जाँच {BsCalendar.Format(dueAt)} मा {Np(facilityName, "मा")} छ।",
                $"{patientName}'s ANC contact {contactNo} is due on {dueAt:yyyy-MM-dd} at {facilityName}.");
    }

    public static (string Np, string En) AncMissed(string patientName, int contactNo, DateOnly dueAt, string? facilityName)
    {
        var no = BsCalendar.NepaliDigits(contactNo.ToString());
        return (
            $"{Np(patientName, "को")} {no} औं गर्भ जाँच ({BsCalendar.Format(dueAt)}) छुटेको छ। कृपया चाँडै {Np(facilityName ?? "स्वास्थ्य संस्था", "मा")} जानुहोस्।",
            $"{patientName} missed ANC contact {contactNo} (due {dueAt:yyyy-MM-dd}). Please visit {facilityName ?? "a health facility"} as soon as possible.");
    }

    public static (string Np, string En) FollowUp(string patientName, DateOnly followUpAt, string? facilityName) =>
        facilityName is null
            ? ($"{Np(patientName, "को")} फलो-अप जाँच {BsCalendar.Format(followUpAt)} मा छ।",
                $"{patientName}'s follow-up visit is due on {followUpAt:yyyy-MM-dd}.")
            : ($"{Np(patientName, "को")} फलो-अप जाँच {BsCalendar.Format(followUpAt)} मा {Np(facilityName, "मा")} छ।",
                $"{patientName}'s follow-up visit is due on {followUpAt:yyyy-MM-dd} at {facilityName}.");

    /// <summary>
    /// A name followed by a Nepali postposition: joined to a Devanagari name ("सीता चौधरीको"), set off by
    /// a space after a name stored in Latin letters ("Sita Chaudhary को").
    /// </summary>
    private static string Np(string name, string postposition) =>
        name.Length > 0 && name[^1] is >= 'ऀ' and <= 'ॿ' ? name + postposition : $"{name} {postposition}";
}
