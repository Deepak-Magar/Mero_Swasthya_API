using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Modules.Maternal.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Ids;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeroSwasthya.Modules.Maternal.Application;

internal static class MaternalMapping
{
    public static AncContactDto ToDto(this AncContact c) => new(
        c.Id, c.PregnancyId, c.ContactNo, c.WeekTarget, c.DueAt, c.DoneAt, c.ProviderUserId, c.Findings,
        c.DangerSigns.ToList(), c.TriageLevel, c.TriageReasons.ToList(), c.Referral, c.Version, c.UpdatedAt, c.Deleted);

    /// <summary>A.2: <c>gestationalAgeDays = today − (edd − 280 d)</c>; <c>nextContact</c> = earliest contact with doneAt = null.</summary>
    public static PregnancyDto ToDto(this Pregnancy p, IEnumerable<AncContact> contacts, IRulesService rules, DateOnly today) => new(
        p.Id, p.PatientId, p.Lmp, p.Edd, p.Gravida, p.Para, p.RiskFactors.ToList(), p.RiskLevel, p.Status, p.BirthPlan,
        p.RegisteredByUserId, rules.GestationalAgeDays(p.Edd, today), NextContact(contacts)?.ToDto(),
        p.Version, p.UpdatedAt, p.Deleted);

    public static DeliveryDto ToDto(this Delivery d) => new(
        d.Id, d.PregnancyId, d.DeliveredAt, d.Place, d.Mode, d.Outcome, d.BabyWeightKg, d.BabySex,
        d.Complications.ToList(), d.Version, d.UpdatedAt, d.Deleted);

    public static AncContact? NextContact(IEnumerable<AncContact> contacts) =>
        contacts.Where(c => c.DoneAt is null && !c.Deleted).OrderBy(c => c.ContactNo).FirstOrDefault();

    public static List<AncContact> Ordered(this IEnumerable<AncContact> contacts) =>
        contacts.Where(c => !c.Deleted).OrderBy(c => c.ContactNo).ToList();
}

/// <summary>A.4 "Maternal": register (idempotent on the client id), read, list, patch.</summary>
internal sealed class PregnancyService(
    MaternalDbContext db,
    IPatientAccess access,
    ICurrentUser currentUser,
    IRulesService rules,
    IFacilityDirectory facilities,
    IClock clock)
{
    public async Task<PregnancyCreatedResponse> CreateAsync(string patientId, CreatePregnancyRequest request, CancellationToken ct)
    {
        // Owner, or a health worker (provider or FCHV) with an active append grant.
        var patient = await access.RequireAppendAsync(patientId, ct);
        var user = await currentUser.GetAsync(ct);

        var existing = await db.Pregnancies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (existing is not null) return await IdempotentAsync(existing, patientId, ct);

        // A.6 #12.
        if (patient.Sex != Sex.Female)
            throw AppException.RuleViolation("Pregnancy can only be registered for a female patient", new { sex = patient.Sex.ToWire() });
        if (await db.Pregnancies.AnyAsync(p => p.PatientId == patientId && !p.Deleted && p.Status == PregnancyStatus.Active, ct))
            throw AppException.RuleViolation("This patient already has an active pregnancy");

        var edd = request.Edd ?? rules.EddFromLmp(request.Lmp!.Value);
        var riskFactors = Clean(request.RiskFactors);
        var now = clock.UtcNow;
        var pregnancy = new Pregnancy
        {
            Id = request.Id!,
            PatientId = patientId,
            Lmp = request.Lmp,
            Edd = edd,
            Gravida = request.Gravida ?? 1,
            Para = request.Para ?? 0,
            RiskFactors = riskFactors,
            RiskLevel = rules.RiskLevelFor(riskFactors),
            Status = PregnancyStatus.Active,
            BirthPlan = await BirthPlanAsync(request.BirthPlan, ct),
            RegisteredByUserId = user.Id,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        };

        // A.8.15: the same eight rows, with the same ids, the app generates offline.
        var anchor = request.Lmp ?? rules.LmpFromEdd(edd);
        var contacts = rules.ScheduleFor(anchor).Select(s => new AncContact
        {
            Id = Ids.AncContactId(pregnancy.Id, s.ContactNo),
            PregnancyId = pregnancy.Id,
            PatientId = patientId,
            ContactNo = s.ContactNo,
            WeekTarget = s.WeekTarget,
            DueAt = s.DueAt,
            Version = 1,
            UpdatedAt = now,
            CreatedAt = now,
        }).ToList();

        db.Pregnancies.Add(pregnancy);
        db.AncContacts.AddRange(contacts);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two retries of the same outbox op raced; answer as if we were second.
            db.ChangeTracker.Clear();
            var winner = await db.Pregnancies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                         ?? throw AppException.RuleViolation("This patient already has an active pregnancy");
            return await IdempotentAsync(winner, patientId, ct);
        }

        return new PregnancyCreatedResponse(pregnancy.ToDto(contacts, rules, clock.TodayUtc), contacts.Select(c => c.ToDto()).ToList());
    }

    public async Task<PregnancyBundleResponse> GetAsync(string id, CancellationToken ct)
    {
        var pregnancy = await LoadAsync(id, ct);
        await access.RequireReadAsync(pregnancy.PatientId, ct);
        var contacts = await ContactsAsync(id, ct);
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.PregnancyId == id && !d.Deleted, ct);
        return new PregnancyBundleResponse(
            pregnancy.ToDto(contacts, rules, clock.TodayUtc), contacts.Select(c => c.ToDto()).ToList(), delivery?.ToDto(), []);
    }

    /// <summary>Additive (not in A.4): every pregnancy of a patient, newest first.</summary>
    public async Task<IReadOnlyList<PregnancyDto>> ListAsync(string patientId, CancellationToken ct)
    {
        await access.RequireReadAsync(patientId, ct);
        var pregnancies = await db.Pregnancies.AsNoTracking()
            .Where(p => p.PatientId == patientId && !p.Deleted)
            .OrderByDescending(p => p.Edd).ThenByDescending(p => p.Id)
            .ToListAsync(ct);
        var ids = pregnancies.Select(p => p.Id).ToList();
        var contacts = await db.AncContacts.AsNoTracking().Where(c => ids.Contains(c.PregnancyId)).ToListAsync(ct);
        var today = clock.TodayUtc;
        return pregnancies.Select(p => p.ToDto(contacts.Where(c => c.PregnancyId == p.Id).Ordered(), rules, today)).ToList();
    }

    /// <summary>A.4 PATCH: <c>version</c> must equal the stored version, else 409 VERSION_CONFLICT with details.current.</summary>
    public async Task<PregnancyDto> PatchAsync(string id, PatchPregnancyRequest request, CancellationToken ct)
    {
        var pregnancy = await LoadAsync(id, ct, track: true);
        await access.RequireAppendAsync(pregnancy.PatientId, ct);
        var contacts = await ContactsAsync(id, ct);
        var today = clock.TodayUtc;

        if (pregnancy.Version != request.Version)
            throw AppException.VersionConflict(pregnancy.ToDto(contacts, rules, today));
        if (pregnancy.Status != PregnancyStatus.Active)
            throw AppException.RuleViolation($"Pregnancy is {pregnancy.Status.ToWire()}; only an active pregnancy can be changed");

        if (request.BirthPlan.HasValue) pregnancy.BirthPlan = await BirthPlanAsync(request.BirthPlan.Value, ct);
        if (request.RiskFactors.HasValue)
        {
            pregnancy.RiskFactors = Clean(request.RiskFactors.Value);
            pregnancy.RiskLevel = rules.RiskLevelFor(pregnancy.RiskFactors);
        }
        if (request.Status.HasValue) pregnancy.Status = PregnancyStatus.Ended;

        pregnancy.Version++;
        pregnancy.UpdatedAt = clock.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else's write landed between our read and our UPDATE … WHERE version = n.
            db.ChangeTracker.Clear();
            var current = await db.Pregnancies.AsNoTracking().FirstAsync(p => p.Id == id, ct);
            throw AppException.VersionConflict(current.ToDto(contacts, rules, today));
        }
        return pregnancy.ToDto(contacts, rules, today);
    }

    internal async Task<Pregnancy> LoadAsync(string id, CancellationToken ct, bool track = false)
    {
        var query = track ? db.Pregnancies : db.Pregnancies.AsNoTracking();
        var pregnancy = await query.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pregnancy is null || pregnancy.Deleted) throw AppException.NotFound("Pregnancy");
        return pregnancy;
    }

    internal async Task<List<AncContact>> ContactsAsync(string pregnancyId, CancellationToken ct) =>
        (await db.AncContacts.AsNoTracking().Where(c => c.PregnancyId == pregnancyId).ToListAsync(ct)).Ordered();

    private async Task<PregnancyCreatedResponse> IdempotentAsync(Pregnancy existing, string patientId, CancellationToken ct)
    {
        if (existing.PatientId != patientId)
            throw AppException.Validation("id", "This id is already used by a pregnancy of another patient");
        var contacts = await ContactsAsync(existing.Id, ct);
        return new PregnancyCreatedResponse(existing.ToDto(contacts, rules, clock.TodayUtc), contacts.Select(c => c.ToDto()).ToList());
    }

    /// <summary>Trims the plan; a known <c>facilityId</c> fills a missing <c>facilityName</c> from Catalog.</summary>
    private async Task<BirthPlanDto?> BirthPlanAsync(BirthPlanRequest? plan, CancellationToken ct)
    {
        if (plan is null) return null;
        var facilityName = Blank(plan.FacilityName);
        var facilityId = Blank(plan.FacilityId);
        if (facilityId is not null)
        {
            var facility = await facilities.FindAsync(facilityId, ct)
                           ?? throw AppException.Validation("birthPlan.facilityId", $"Unknown facility \"{facilityId}\"");
            facilityName ??= facility.Name;
        }
        return new BirthPlanDto
        {
            FacilityId = facilityId,
            FacilityName = facilityName,
            Transport = Blank(plan.Transport),
            MoneySaved = plan.MoneySaved,
            BloodDonorName = Blank(plan.BloodDonorName),
            BloodDonorPhone = Blank(plan.BloodDonorPhone),
            CompanionName = Blank(plan.CompanionName),
        };
    }

    private static List<string> Clean(IEnumerable<string>? items) =>
        items?.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal).ToList() ?? [];

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
