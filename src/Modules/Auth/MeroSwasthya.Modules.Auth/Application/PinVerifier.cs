using MeroSwasthya.Modules.Auth.Contracts;
using MeroSwasthya.Modules.Auth.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Auth.Application;

internal sealed class PinVerifier(AuthDbContext db, PinHasher pins, IClock clock) : IPinVerifier
{
    public async Task<bool> VerifyAsync(string userId, string pin, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.PinHash is null) return false;

        var now = clock.UtcNow;
        if (user.PinLockedUntil is { } until && until > now)
            throw AppException.RateLimited("Too many wrong PINs; try again in 15 minutes");

        if (pins.Verify(pin, user.PinHash))
        {
            if (user.FailedPinAttempts != 0)
            {
                user.FailedPinAttempts = 0;
                await db.SaveChangesAsync(ct);
            }
            return true;
        }

        user.FailedPinAttempts++;
        if (user.FailedPinAttempts >= AuthService.PinMaxFailures)
        {
            user.PinLockedUntil = now + AuthService.PinLockout;
            user.FailedPinAttempts = 0;
        }
        await db.SaveChangesAsync(ct);
        return false;
    }
}
