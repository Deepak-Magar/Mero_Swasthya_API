using MeroSwasthya.Modules.Maternal.Application;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Infrastructure;

/// <summary>
/// Migrates schema <c>maternal</c> and seeds Sita's pregnancy exactly as the app's mock does: LMP 30 weeks
/// ago (so she is at week 30 whenever the demo runs), the Part A pregnancy id, eight contacts with the
/// deterministic ids, contacts 1–3 recorded by the seeded provider 18 / 10 / 4 weeks ago (all green).
/// Insert-if-missing; <c>updatedAt</c> = now (addendum §7).
/// </summary>
internal sealed class MaternalModuleInitializer(MaternalDbContext db, IRulesService rules, IClock clock) : IModuleInitializer
{
    public const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    public const string SitaPregnancyId = "pg_b2b2b2b2-0000-4000-8000-000000000001";
    public const string ProviderId = "u_22222222-2222-4222-8222-222222222222";
    public const int SitaWeeksPregnant = 30;

    public string Module => "maternal";

    /// <summary>After Patients (30) and Clinical (40): the seed refers to seeded patients.</summary>
    public int Order => 50;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Pregnancies.AnyAsync(p => p.Id == SitaPregnancyId, ct)) return;

        var now = clock.UtcNow;
        var today = clock.TodayUtc;
        var lmp = today.AddDays(-SitaWeeksPregnant * 7);

        var pregnancy = new Pregnancy
        {
            Id = SitaPregnancyId,
            PatientId = SitaId,
            Lmp = lmp,
            Edd = rules.EddFromLmp(lmp),
            Gravida = 1,
            Para = 0,
            RiskFactors = [],
            RiskLevel = RiskLevel.Normal,
            Status = PregnancyStatus.Active,
            BirthPlan = null,
            RegisteredByUserId = ProviderId,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        };

        var contacts = rules.ScheduleFor(lmp).Select(s => new AncContact
        {
            Id = Ids.AncContactId(SitaPregnancyId, s.ContactNo),
            PregnancyId = SitaPregnancyId,
            PatientId = SitaId,
            ContactNo = s.ContactNo,
            WeekTarget = s.WeekTarget,
            DueAt = s.DueAt,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        }).ToDictionary(c => c.ContactNo);

        // Contacts 1–3 already happened: at week 30 a real record would not be blank.
        Done(contacts[1], weeksAgo: 18, new FindingsDto
        {
            WeightKg = 52.0, BpSys = 110, BpDia = 70, HbGdl = 11.8, UrineProtein = UrineProtein.Neg, IfaGiven = true, TdDoseGiven = true,
        });
        Done(contacts[2], weeksAgo: 10, new FindingsDto
        {
            WeightKg = 55.5, BpSys = 118, BpDia = 76, FundalHeightCm = 20.0, IfaGiven = true, CalciumGiven = true, DewormingGiven = true,
        });
        Done(contacts[3], weeksAgo: 4, new FindingsDto
        {
            WeightKg = 58.0, BpSys = 124, BpDia = 80, FundalHeightCm = 26.0, FhrBpm = 142, IfaGiven = true, CalciumGiven = true,
            FetalMovement = FetalMovement.Normal,
        });

        db.Pregnancies.Add(pregnancy);
        db.AncContacts.AddRange(contacts.Values);
        await db.SaveChangesAsync(ct);

        void Done(AncContact contact, int weeksAgo, FindingsDto findings)
        {
            // 04:05 UTC = 09:50 Nepal time — clinic hours (as the Clinical seed).
            var doneAt = today.AddDays(-weeksAgo * 7).ToDateTime(new TimeOnly(4, 5), DateTimeKind.Utc);
            var triage = rules.Triage(new TriageInput(findings, [], pregnancy.RiskLevel,
                rules.GestationalAgeDays(pregnancy.Edd, DateOnly.FromDateTime(doneAt))));
            contact.DoneAt = doneAt;
            contact.ProviderUserId = ProviderId;
            contact.Findings = findings;
            contact.DangerSigns = [];
            contact.TriageLevel = triage.Level;
            contact.TriageReasons = triage.ReasonsEn.ToList();
            contact.Version = 2;
        }
    }
}
