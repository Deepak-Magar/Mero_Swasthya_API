using MeroSwasthya.Shared.Security;

namespace MeroSwasthya.Modules.Auth.Contracts;

/// <summary>
/// Read-only user lookups for other modules (e.g. Grants writes actorName into AuditEntry, Reminders
/// needs a patient-owner phone). The only way into Auth data from outside the module.
/// </summary>
public interface IUserDirectory
{
    Task<CurrentUserInfo?> FindAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, CurrentUserInfo>> FindManyAsync(IEnumerable<string> userIds, CancellationToken ct = default);
}
