using MeroSwasthya.Shared.Security;

namespace MeroSwasthya.Modules.Auth.Domain;

/// <summary>A.2 User plus the credential state that never leaves the server.</summary>
internal sealed class User
{
    public required string Id { get; init; }
    public required string Phone { get; init; }
    public UserRole Role { get; set; } = UserRole.Patient;
    public required string Name { get; set; }
    public string? FacilityId { get; set; }
    public string? FacilityName { get; set; }

    /// <summary>Argon2id PHC string; null until the user sets a PIN.</summary>
    public string? PinHash { get; set; }
    public int FailedPinAttempts { get; set; }
    public DateTime? PinLockedUntil { get; set; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One OTP request. Rows double as the rate-limit log (5 per phone per 10 min).</summary>
internal sealed class OtpChallenge
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Phone { get; init; }
    public required string CodeHash { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? ConsumedAt { get; set; }
    public int FailedAttempts { get; set; }
}

/// <summary>Opaque 30-day refresh token, stored only as a SHA-256 hash; rotated on every use.</summary>
internal sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required string TokenHash { get; init; }

    /// <summary>All tokens descended from one login. Re-using a rotated token revokes the whole family.</summary>
    public Guid FamilyId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
}

/// <summary>Seeded invite that upgrades an account to provider / FCHV at a facility (A.4 /auth/provider/activate).</summary>
internal sealed class InviteCode
{
    public required string Code { get; init; }
    public UserRole Role { get; init; }
    public required string FacilityId { get; init; }
    public required string FacilityName { get; init; }
    public bool Active { get; set; } = true;

    /// <summary>Null = unlimited (demo codes are shared by the whole team).</summary>
    public int? MaxUses { get; init; }
    public int UseCount { get; set; }
    public DateTime CreatedAt { get; init; }
}
