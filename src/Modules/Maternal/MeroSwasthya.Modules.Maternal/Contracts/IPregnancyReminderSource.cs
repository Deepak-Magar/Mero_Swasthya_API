namespace MeroSwasthya.Modules.Maternal.Contracts;

/// <summary>
/// Implemented by the Reminders module: the A.2 Reminder DTOs scheduled for one pregnancy, for the
/// <c>reminders</c> list in GET /pregnancies/:id. With no implementation registered the list is empty.
/// </summary>
public interface IPregnancyReminderSource
{
    Task<IReadOnlyList<object>> ForPregnancyAsync(string pregnancyId, CancellationToken ct = default);
}
