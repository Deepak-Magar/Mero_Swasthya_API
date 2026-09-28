namespace MeroSwasthya.Modules.Auth.Contracts;

/// <summary>
/// Checks a user's PIN without issuing a session — used by Grants for the A.7 printed-card redeem,
/// where the patient says their PIN to the health worker. Shares the login lockout: 5 wrong PINs
/// lock the account for 15 minutes (throws 429 RATE_LIMITED while locked).
/// </summary>
public interface IPinVerifier
{
    Task<bool> VerifyAsync(string userId, string pin, CancellationToken ct = default);
}
