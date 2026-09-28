using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeroSwasthya.Modules.Grants.Application;

internal sealed record GrantClaims(string GrantId, string PatientId, GrantScope Scope, string Jti);

/// <summary>
/// A.7 grant tokens: HS256 with the GRANT key (never the access key), claims
/// <c>{ typ:"grant", gid, pid, scope, iat, exp, jti }</c>, exp = iat + ttlMinutes·60.
/// The QR payload is <c>"SWC1:" + token</c>.
/// </summary>
internal sealed class GrantTokens(JwtSettings settings, SigningKeys keys)
{
    public const string QrPrefix = "SWC1:";
    public const string Audience = "mero-swasthya-grant";
    public const string TokenType = "grant";

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string Create(AccessGrant grant) => _handler.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = settings.Issuer,
        Audience = Audience,
        IssuedAt = grant.CreatedAt,
        NotBefore = grant.CreatedAt,
        Expires = grant.ExpiresAt,
        Claims = new Dictionary<string, object>
        {
            ["typ"] = TokenType,
            ["gid"] = grant.Id,
            ["pid"] = grant.PatientId,
            ["scope"] = grant.Scope.ToWire(),
            [JwtRegisteredClaimNames.Jti] = grant.Jti,
        },
        SigningCredentials = new SigningCredentials(keys.Grant, SecurityAlgorithms.HmacSha256),
    });

    /// <summary>
    /// Signature, issuer, audience and <c>typ</c> are checked here; expiry is decided by the stored row
    /// (so an idempotent re-scan by the same provider still works, and the answer is GRANT_EXPIRED
    /// rather than a generic token error). Returns null for anything that is not our grant token.
    /// </summary>
    public async Task<GrantClaims?> ReadAsync(string qrPayload)
    {
        if (!qrPayload.StartsWith(QrPrefix, StringComparison.Ordinal)) return null;
        var result = await _handler.ValidateTokenAsync(qrPayload[QrPrefix.Length..], new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = keys.Grant,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = false,
        });
        if (!result.IsValid) return null;

        string? Claim(string name) => result.Claims.TryGetValue(name, out var v) ? v?.ToString() : null;
        if (Claim("typ") != TokenType) return null;
        var gid = Claim("gid");
        var pid = Claim("pid");
        var jti = Claim(JwtRegisteredClaimNames.Jti);
        if (gid is null || pid is null || jti is null || !WireEnum.TryParse<GrantScope>(Claim("scope"), out var scope))
            return null;
        return new GrantClaims(gid, pid, scope, jti);
    }
}
