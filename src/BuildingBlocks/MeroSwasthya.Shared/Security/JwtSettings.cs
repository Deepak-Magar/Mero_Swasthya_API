using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MeroSwasthya.Shared.Security;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtSettings
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "mero-swasthya";

    /// <summary>Audience of 12 h access tokens.</summary>
    public string AccessAudience { get; set; } = "mero-swasthya-api";

    /// <summary>Audience of the 10 min <c>tempToken</c> issued by OTP verify; only accepted by <c>/auth/pin/set</c>.</summary>
    public string TempAudience { get; set; } = "mero-swasthya-otp";

    /// <summary>HS256 key for access + temp tokens (≥ 32 bytes).</summary>
    public string AccessSigningKey { get; set; } = "";

    /// <summary>HS256 key for grant (QR) tokens, A.7 GRANT_SECRET (≥ 32 bytes). Never accepted as a bearer token.</summary>
    public string GrantSigningKey { get; set; } = "";

    public int AccessTokenHours { get; set; } = 12;
    public int TempTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>
/// The two HS256 keys, kept separate so a grant token can never authenticate an API call and an
/// access token can never be redeemed as a grant. The grant key is used by the Grants module.
/// </summary>
public sealed class SigningKeys
{
    public SigningKeys(JwtSettings settings)
    {
        Access = Build(settings.AccessSigningKey, "Jwt:AccessSigningKey");
        Grant = Build(settings.GrantSigningKey, "Jwt:GrantSigningKey");
        if (settings.AccessSigningKey == settings.GrantSigningKey)
            throw new InvalidOperationException("Jwt:AccessSigningKey and Jwt:GrantSigningKey must differ.");
    }

    public SymmetricSecurityKey Access { get; }
    public SymmetricSecurityKey Grant { get; }

    private static SymmetricSecurityKey Build(string secret, string name)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException($"{name} must be configured with at least 32 bytes.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = name };
    }
}
