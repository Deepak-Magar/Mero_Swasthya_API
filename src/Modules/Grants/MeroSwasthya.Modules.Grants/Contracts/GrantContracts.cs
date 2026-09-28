using System.Text.Json.Serialization;
using MeroSwasthya.Modules.Grants.Domain;

namespace MeroSwasthya.Modules.Grants.Contracts;

/// <summary>
/// A.2 AccessGrant. <c>token</c> is present only in the POST /grants response ("returned ONLY on
/// creation"); <c>longLived</c> and <c>sections</c> are the additive fields the app model reads
/// (mock_api.dart, contract addendum §4).
/// </summary>
public sealed record GrantDto(
    string Id,
    string PatientId,
    GrantScope Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Token,
    DateTime ExpiresAt,
    string? RedeemedByUserId,
    DateTime? RedeemedAt,
    DateTime? RevokedAt,
    DateTime? AccessUntil,
    bool LongLived,
    IReadOnlyList<GrantSection> Sections);

/// <summary>
/// Authorization helpers for other modules. "Active" = redeemed by this user, not revoked, and
/// <c>accessUntil</c> in the future. Owners always pass. Endpoints that need the A.3 error
/// distinction (FORBIDDEN vs GRANT_EXPIRED) should use <c>Patients.Contracts.IPatientAccess</c>,
/// which consults the same grants through <c>IPatientGrantSource</c>.
/// </summary>
public interface IGrantAuthorization
{
    /// <summary>Owner OR an active redeemed grant of any scope.</summary>
    Task<bool> CanReadPatientAsync(string userId, string patientId, CancellationToken ct = default);

    /// <summary>Owner OR an active redeemed grant with scope append.</summary>
    Task<bool> CanAppendPatientAsync(string userId, string patientId, CancellationToken ct = default);
}
