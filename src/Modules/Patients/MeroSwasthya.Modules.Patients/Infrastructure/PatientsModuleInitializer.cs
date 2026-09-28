using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Patients.Infrastructure;

/// <summary>
/// Migrates schema <c>patients</c> and seeds the demo household owned by +9779801000001
/// (Part A ids). Insert-if-missing, and <c>updatedAt</c> = now so an already-synced phone still
/// pulls the rows (contract addendum §7).
/// </summary>
internal sealed class PatientsModuleInitializer(PatientsDbContext db, IClock clock) : IModuleInitializer
{
    public const string OwnerUserId = "u_11111111-1111-4111-8111-111111111111";
    public const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    public const string RamId = "p_a1a1a1a1-0000-4000-8000-000000000002";
    public const string AaravId = "p_a1a1a1a1-0000-4000-8000-000000000006";

    public string Module => "patients";
    public int Order => 30;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = clock.TodayUtc;
        // Aarav is "three years and two months old" (addendum §7): born on the 14th, 38 months ago.
        var aaravDob = new DateOnly(today.AddMonths(-38).Year, today.AddMonths(-38).Month, 14);

        Patient Make(string id, string name, Sex sex, DateOnly dob, string bloodGroup, List<string> allergies, List<string> chronic) =>
            new()
            {
                Id = id,
                OwnerUserId = OwnerUserId,
                Name = name,
                Sex = sex,
                Dob = dob,
                BloodGroup = bloodGroup,
                Ward = 5,
                Municipality = "Ghorahi",
                Allergies = allergies,
                ChronicConditions = chronic,
                EmergencyContactPhone = "+9779801000009",
                Version = 1,
                UpdatedAt = now,
                CreatedAt = now,
            };

        var seed = new[]
        {
            Make(SitaId, "Sita Chaudhary", Sex.Female, new DateOnly(2002, 4, 11), "B+", ["sulpha"], []),
            Make(RamId, "Ram Bahadur Chaudhary", Sex.Male, new DateOnly(1968, 1, 15), "O+", ["penicillin"], ["E11", "I10"]),
            Make(AaravId, "Aarav Chaudhary", Sex.Male, aaravDob, "B+", [], []),
        };

        foreach (var patient in seed)
        {
            if (!await db.Patients.AnyAsync(p => p.Id == patient.Id, ct))
                db.Patients.Add(patient);
        }
        await db.SaveChangesAsync(ct);

        // TODO(Maternal): Sita's week-30 pregnancy pg_b2b2b2b2-0000-4000-8000-000000000001 with contacts 1–3 done;
        //                 Aarav's immunisation schedule (MR-2 and TCV overdue) and three growth measurements.
        // TODO(Clinical): Ram's visit v_c3c3c3c3-0000-4000-8000-000000000001 (E11, Metformin) and his two documents
        //                 d_e5e5e5e5-…-0001 (lab) and d_e5e5e5e5-…-0002 ("Bharatpur Hospital discharge sheet").
        // TODO(Grants):   the three dashboard women p_…0003–0005 reached through already-redeemed grants.
        // TODO(Reminders): the two seeded anc_due reminders that /demo/sms shows.
    }
}
