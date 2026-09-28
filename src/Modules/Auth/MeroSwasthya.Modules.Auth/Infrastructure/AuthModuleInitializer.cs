using MeroSwasthya.Modules.Auth.Application;
using MeroSwasthya.Modules.Auth.Domain;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Auth.Infrastructure;

/// <summary>Migrates schema <c>auth</c> and seeds the demo accounts and invite codes (idempotent: insert-if-missing).</summary>
internal sealed class AuthModuleInitializer(AuthDbContext db, PinHasher pins, IClock clock) : IModuleInitializer
{
    public const string PatientUserId = "u_11111111-1111-4111-8111-111111111111";
    public const string ProviderUserId = "u_22222222-2222-4222-8222-222222222222";
    public const string DemoPin = "1234";

    public string Module => "auth";
    public int Order => 20;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        var users = new[]
        {
            new User
            {
                Id = PatientUserId, Phone = "+9779801000001", Role = UserRole.Patient, Name = "Sita Chaudhary",
                CreatedAt = now, UpdatedAt = now,
            },
            new User
            {
                Id = ProviderUserId, Phone = "+9779801000002", Role = UserRole.Provider, Name = "Ramesh Thapa (HA)",
                FacilityId = "f_0001", FacilityName = "Ghorahi Health Post", CreatedAt = now, UpdatedAt = now,
            },
        };
        foreach (var user in users)
        {
            if (await db.Users.AnyAsync(u => u.Id == user.Id || u.Phone == user.Phone, ct)) continue;
            user.PinHash = pins.Hash(DemoPin);
            db.Users.Add(user);
        }

        var invites = new[]
        {
            new InviteCode
            {
                Code = "HA-GHORAHI-01", Role = UserRole.Provider, FacilityId = "f_0001",
                FacilityName = "Ghorahi Health Post", CreatedAt = now,
            },
            new InviteCode
            {
                Code = "FCHV-W5-01", Role = UserRole.Fchv, FacilityId = "f_0001",
                FacilityName = "Ghorahi Health Post", CreatedAt = now,
            },
        };
        foreach (var invite in invites)
        {
            if (!await db.InviteCodes.AnyAsync(i => i.Code == invite.Code, ct))
                db.InviteCodes.Add(invite);
        }

        await db.SaveChangesAsync(ct);
    }
}
