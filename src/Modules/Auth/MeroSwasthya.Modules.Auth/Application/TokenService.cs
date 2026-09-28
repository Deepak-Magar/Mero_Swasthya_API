using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeroSwasthya.Modules.Auth.Application;

internal static class TokenTypes
{
    public const string Claim = "typ";
    public const string Access = "access";
    public const string Temp = "temp";
}

/// <summary>Issues access JWTs (12 h), OTP temp JWTs (10 min) and opaque refresh tokens (30 d).</summary>
internal sealed class TokenService(JwtSettings settings, SigningKeys keys, IClock clock)
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string AccessToken(User user)
    {
        var now = clock.UtcNow;
        return Create(settings.AccessAudience, now, now.AddHours(settings.AccessTokenHours), new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id,
            [TokenTypes.Claim] = TokenTypes.Access,
            // Informational only: authorisation always re-reads the stored role (it changes on activation).
            ["role"] = user.Role.ToWire(),
        });
    }

    /// <summary>Proof that the phone passed OTP; accepted only by POST /auth/pin/set.</summary>
    public string TempToken(string phone)
    {
        var now = clock.UtcNow;
        return Create(settings.TempAudience, now, now.AddMinutes(settings.TempTokenMinutes), new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = phone,
            [TokenTypes.Claim] = TokenTypes.Temp,
        });
    }

    public static string NewRefreshToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public DateTime RefreshExpiry() => clock.UtcNow.AddDays(settings.RefreshTokenDays);

    private string Create(string audience, DateTime issuedAt, DateTime expires, IDictionary<string, object> claims)
    {
        claims[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N");
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(keys.Access, SecurityAlgorithms.HmacSha256),
        });
    }

    public static string? Subject(ClaimsPrincipal principal) => principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
