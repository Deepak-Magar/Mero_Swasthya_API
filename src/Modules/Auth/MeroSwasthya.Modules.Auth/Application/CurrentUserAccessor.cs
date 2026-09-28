using MeroSwasthya.Modules.Auth.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Auth.Application;

/// <summary>
/// <see cref="ICurrentUser"/> for every module: the user id comes from the access token, the role and
/// facility from the database (cached per request), because activation changes them mid-session.
/// </summary>
internal sealed class CurrentUserAccessor(IHttpContextAccessor http, AuthDbContext db) : ICurrentUser
{
    private CurrentUserInfo? _cached;

    public string? UserId
    {
        get
        {
            var principal = http.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;
            return principal.FindFirst(TokenTypes.Claim)?.Value == TokenTypes.Access ? TokenService.Subject(principal) : null;
        }
    }

    public async Task<CurrentUserInfo> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var id = UserId ?? throw AppException.Unauthenticated();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw AppException.Unauthenticated("Account no longer exists");
        return _cached = new CurrentUserInfo(user.Id, user.Phone, user.Role, user.Name, user.FacilityId, user.FacilityName);
    }
}
