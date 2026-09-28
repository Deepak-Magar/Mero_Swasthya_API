using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Shared.Security;

/// <summary>A.2 User.role.</summary>
public enum UserRole
{
    [WireName("patient")] Patient,
    [WireName("provider")] Provider,
    [WireName("fchv")] Fchv,
    [WireName("admin")] Admin,
}

/// <summary>The caller, as currently stored (not as frozen in the token: a role can change after activation).</summary>
public sealed record CurrentUserInfo(
    string Id,
    string Phone,
    UserRole Role,
    string Name,
    string? FacilityId,
    string? FacilityName)
{
    /// <summary>Provider and FCHV are the two health-worker roles that redeem grants (A.4).</summary>
    public bool IsHealthWorker => Role is UserRole.Provider or UserRole.Fchv;
}

/// <summary>
/// Request-scoped access to the authenticated caller. <see cref="UserId"/> comes from the access
/// token; <see cref="GetAsync"/> loads the stored user (implemented by the Auth module).
/// </summary>
public interface ICurrentUser
{
    /// <summary>The <c>sub</c> of a valid access token, or null.</summary>
    string? UserId { get; }

    /// <summary>The stored user; 401 UNAUTHENTICATED when there is no valid token or the user no longer exists.</summary>
    Task<CurrentUserInfo> GetAsync(CancellationToken ct = default);
}

public static class Authz
{
    /// <summary>403 FORBIDDEN unless the caller has one of <paramref name="roles"/>.</summary>
    public static CurrentUserInfo RequireRole(this CurrentUserInfo user, params UserRole[] roles)
    {
        if (!roles.Contains(user.Role))
            throw AppException.Forbidden($"Role '{user.Role.ToWire()}' is not allowed to do this");
        return user;
    }
}
