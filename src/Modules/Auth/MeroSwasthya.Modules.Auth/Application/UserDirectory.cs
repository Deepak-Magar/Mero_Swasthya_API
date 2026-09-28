using MeroSwasthya.Modules.Auth.Contracts;
using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Modules.Auth.Infrastructure;
using MeroSwasthya.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Auth.Application;

internal sealed class UserDirectory(AuthDbContext db) : IUserDirectory
{
    public async Task<CurrentUserInfo?> FindAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : ToInfo(user);
    }

    public async Task<IReadOnlyDictionary<string, CurrentUserInfo>> FindManyAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, ToInfo);
    }

    private static CurrentUserInfo ToInfo(User u) => new(u.Id, u.Phone, u.Role, u.Name, u.FacilityId, u.FacilityName);
}
