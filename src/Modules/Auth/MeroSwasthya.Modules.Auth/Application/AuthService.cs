using System.Security.Cryptography;
using System.Text;
using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Modules.Auth.Infrastructure;
using MeroSwasthya.Shared;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Modules.Auth.Application;

/// <summary>A.4 "Auth": OTP → (set PIN | PIN login) → access + refresh, refresh rotation, invite activation.</summary>
internal sealed class AuthService(
    AuthDbContext db,
    TokenService tokens,
    PinHasher pins,
    FeatureFlags features,
    IClock clock,
    ILogger<AuthService> logger)
{
    public const string DemoOtp = "123456";
    public const int OtpTtlSeconds = 300;
    public const int OtpRequestsPerWindow = 5;
    public static readonly TimeSpan OtpWindow = TimeSpan.FromMinutes(10);
    public const int OtpMaxAttempts = 5;
    public const int PinMaxFailures = 5;
    public static readonly TimeSpan PinLockout = TimeSpan.FromMinutes(15);

    public async Task<OtpRequestResponse> RequestOtpAsync(OtpRequest request, CancellationToken ct)
    {
        var phone = Phones.Normalize(request.Phone);
        var now = clock.UtcNow;

        // A.4: 5 per phone per 10 min → 429 RATE_LIMITED.
        var recent = await db.OtpChallenges.CountAsync(c => c.Phone == phone && c.CreatedAt > now - OtpWindow, ct);
        if (recent >= OtpRequestsPerWindow)
            throw AppException.RateLimited("Too many OTP requests for this phone; try again in 10 minutes");

        var code = features.OtpDemo ? DemoOtp : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.OtpChallenges.Add(new OtpChallenge
        {
            Phone = phone,
            CodeHash = HashOtp(phone, code),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(OtpTtlSeconds),
        });
        await db.SaveChangesAsync(ct);

        if (!features.SmsIsMock)
            // TODO(Reminders): hand the code to the SMS gateway once the Reminders module owns outbound SMS.
            logger.LogWarning("SMS mode {Mode} has no gateway yet; OTP for {Phone} was not sent", features.SmsMode, phone);

        // A.4: demoOtp present only when SMS_MODE=mock.
        return new OtpRequestResponse(phone, OtpTtlSeconds, features.SmsIsMock ? code : null);
    }

    public async Task<OtpVerifyResponse> VerifyOtpAsync(OtpVerifyRequest request, CancellationToken ct)
    {
        var phone = Phones.Normalize(request.Phone);
        var now = clock.UtcNow;

        var challenge = await db.OtpChallenges
            .Where(c => c.Phone == phone && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.FailedAttempts >= OtpMaxAttempts)
            throw AppException.Validation("otp", "Code expired or not requested; request a new code");

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(challenge.CodeHash), Encoding.ASCII.GetBytes(HashOtp(phone, request.Otp!))))
        {
            challenge.FailedAttempts++;
            await db.SaveChangesAsync(ct);
            throw AppException.Validation("otp", "Wrong code");
        }

        challenge.ConsumedAt = now;
        await db.SaveChangesAsync(ct);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == phone, ct);
        return new OtpVerifyResponse(tokens.TempToken(phone), HasPin: user?.PinHash is not null, IsNewUser: user is null);
    }

    /// <summary>Authenticated by the tempToken: creates the account if new, then sets (or resets) the PIN.</summary>
    public async Task<SessionResponse> SetPinAsync(string phone, PinSetRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var name = request.Name?.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct);

        if (user is null)
        {
            if (string.IsNullOrEmpty(name))
                throw AppException.Validation("name", "'name' is required for a new account");
            user = new User { Id = Ids.New("u"), Phone = phone, Name = name, CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            user.Name = name;
        }

        user.PinHash = pins.Hash(request.Pin!);
        user.FailedPinAttempts = 0;
        user.PinLockedUntil = null;
        user.UpdatedAt = now;

        var session = IssueSession(user, familyId: Guid.NewGuid());
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<SessionResponse> LoginAsync(PinLoginRequest request, CancellationToken ct)
    {
        var phone = Phones.Normalize(request.Phone);
        var now = clock.UtcNow;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct);

        // Same answer for "no such phone" and "wrong PIN": no account enumeration.
        if (user?.PinHash is null)
            throw AppException.Validation("pin", "Wrong phone number or PIN");

        if (user.PinLockedUntil is { } until && until > now)
            throw AppException.RateLimited("Too many wrong PINs; try again in 15 minutes");

        if (!pins.Verify(request.Pin!, user.PinHash))
        {
            user.FailedPinAttempts++;
            // A.4: 5 wrong PINs → 429 for 15 min.
            var locked = user.FailedPinAttempts >= PinMaxFailures;
            if (locked)
            {
                user.PinLockedUntil = now + PinLockout;
                user.FailedPinAttempts = 0;
            }
            await db.SaveChangesAsync(ct);
            throw locked
                ? AppException.RateLimited("Too many wrong PINs; try again in 15 minutes")
                : AppException.Validation("pin", "Wrong phone number or PIN");
        }

        user.FailedPinAttempts = 0;
        user.PinLockedUntil = null;
        var session = IssueSession(user, familyId: Guid.NewGuid());
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Rotation: the presented token is revoked and replaced. Presenting a revoked token revokes its family.</summary>
    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = TokenService.HashRefreshToken(request.RefreshToken!);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null)
            throw AppException.Unauthenticated("Refresh token is invalid");

        if (stored.RevokedAt is not null)
        {
            if (stored.ReplacedByHash is not null)
            {
                logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking family {Family}",
                    stored.UserId, stored.FamilyId);
                await db.RefreshTokens
                    .Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            }
            throw AppException.Unauthenticated("Refresh token has been revoked");
        }

        if (stored.ExpiresAt <= now)
            throw AppException.Unauthenticated("Refresh token has expired");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, ct)
                   ?? throw AppException.Unauthenticated("Account no longer exists");

        var session = IssueSession(user, stored.FamilyId);
        stored.RevokedAt = now;
        stored.ReplacedByHash = TokenService.HashRefreshToken(session.RefreshToken);
        await db.SaveChangesAsync(ct);
        return new RefreshResponse(session.AccessToken, session.RefreshToken);
    }

    public async Task<UserDto> ActivateAsync(string userId, ActivateRequest request, CancellationToken ct)
    {
        var code = request.InviteCode!.Trim().ToUpperInvariant();
        var invite = await db.InviteCodes.FirstOrDefaultAsync(i => i.Code == code && i.Active, ct);
        if (invite is null || (invite.MaxUses is { } max && invite.UseCount >= max))
            throw AppException.Validation("inviteCode", "Unknown invite code");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.Unauthenticated("Account no longer exists");

        user.Role = invite.Role;
        user.FacilityId = invite.FacilityId;
        user.FacilityName = invite.FacilityName;
        user.UpdatedAt = clock.UtcNow;
        invite.UseCount++;
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    public async Task<UserDto> MeAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.Unauthenticated("Account no longer exists");
        return UserDto.From(user);
    }

    private SessionResponse IssueSession(User user, Guid familyId)
    {
        var refresh = TokenService.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = TokenService.HashRefreshToken(refresh),
            FamilyId = familyId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = tokens.RefreshExpiry(),
        });
        return new SessionResponse(tokens.AccessToken(user), refresh, UserDto.From(user));
    }

    private static string HashOtp(string phone, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}:{code}"))).ToLowerInvariant();
}
