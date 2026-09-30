using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Modules.Maternal.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>The patient's active pregnancy (at most one) with its contacts, as DTOs.</summary>
internal sealed class ActivePregnancyQuery(MaternalDbContext db, IRulesService rules, IClock clock)
{
    public async Task<(PregnancyDto Pregnancy, IReadOnlyList<AncContactDto> Contacts)?> GetAsync(string patientId, CancellationToken ct)
    {
        var pregnancy = await db.Pregnancies.AsNoTracking()
            .Where(p => p.PatientId == patientId && !p.Deleted && p.Status == PregnancyStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (pregnancy is null) return null;

        var contacts = (await db.AncContacts.AsNoTracking().Where(c => c.PregnancyId == pregnancy.Id).ToListAsync(ct)).Ordered();
        return (pregnancy.ToDto(contacts, rules, clock.TodayUtc), contacts.Select(c => c.ToDto()).ToList());
    }
}

/// <summary>Any pregnancy by id, without access checks — for the Reminders module's scheduling.</summary>
internal sealed class PregnancyDirectory(MaternalDbContext db, IRulesService rules, IClock clock) : IPregnancyDirectory
{
    public async Task<PregnancySnapshot?> FindAsync(string pregnancyId, CancellationToken ct = default)
    {
        var pregnancy = await db.Pregnancies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pregnancyId && !p.Deleted, ct);
        if (pregnancy is null) return null;

        var contacts = (await db.AncContacts.AsNoTracking().Where(c => c.PregnancyId == pregnancyId).ToListAsync(ct)).Ordered();
        return new PregnancySnapshot(pregnancy.ToDto(contacts, rules, clock.TodayUtc), contacts.Select(c => c.ToDto()).ToList());
    }
}

/// <summary>A.4 summary.activePregnancy (Order 20, after Clinical).</summary>
internal sealed class MaternalSummaryContributor(ActivePregnancyQuery query) : IPatientSummaryContributor
{
    public int Order => 20;

    public async Task ContributeAsync(PatientSummaryBuilder summary, CancellationToken ct = default)
    {
        var active = await query.GetAsync(summary.Patient.Id, ct);
        if (active is not null) summary.ActivePregnancy = active.Value.Pregnancy;
    }
}

/// <summary>Fills <c>pregnancy</c> and <c>ancContacts</c> in the grant redeem bundle.</summary>
internal sealed class ActivePregnancySource(ActivePregnancyQuery query) : IActivePregnancySource
{
    public async Task<ActivePregnancySnapshot?> GetAsync(string patientId, CancellationToken ct = default)
    {
        var active = await query.GetAsync(patientId, ct);
        return active is null ? null : new ActivePregnancySnapshot(active.Value.Pregnancy, active.Value.Contacts.Cast<object>().ToList());
    }
}

/// <summary>Timeline kinds <c>pregnancy_registered</c>, <c>anc_contact</c> (recorded contacts only, badge = triage) and <c>delivery</c>.</summary>
internal sealed class MaternalTimelineContributor(MaternalDbContext db, IRulesService rules, IClock clock) : ITimelineContributor
{
    private static readonly Dictionary<DeliveryPlace, string> PlaceLabels = new()
    {
        [DeliveryPlace.Home] = "Home",
        [DeliveryPlace.BirthingCentre] = "Birthing centre",
        [DeliveryPlace.Hospital] = "Hospital",
        [DeliveryPlace.OnTheWay] = "On the way",
    };

    private static readonly Dictionary<DeliveryMode, string> ModeLabels = new()
    {
        [DeliveryMode.Normal] = "Normal delivery",
        [DeliveryMode.Assisted] = "Assisted delivery",
        [DeliveryMode.Cs] = "Caesarean section",
    };

    public async Task<IReadOnlyList<TimelineItemDto>> GetAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default)
    {
        var today = clock.TodayUtc;

        var pregnancyQuery = db.Pregnancies.AsNoTracking().Where(p => p.PatientId == patientId && !p.Deleted);
        if (before is not null) pregnancyQuery = pregnancyQuery.Where(p => p.CreatedAt < before);
        var pregnancies = await pregnancyQuery.OrderByDescending(p => p.CreatedAt).Take(limit).ToListAsync(ct);

        var contactQuery = db.AncContacts.AsNoTracking().Where(c => c.PatientId == patientId && !c.Deleted && c.DoneAt != null);
        if (before is not null) contactQuery = contactQuery.Where(c => c.DoneAt < before);
        var contacts = await contactQuery.OrderByDescending(c => c.DoneAt).Take(limit).ToListAsync(ct);

        var deliveryQuery = db.Deliveries.AsNoTracking().Where(d => d.PatientId == patientId && !d.Deleted);
        if (before is not null) deliveryQuery = deliveryQuery.Where(d => d.DeliveredAt < before);
        var deliveries = await deliveryQuery.OrderByDescending(d => d.DeliveredAt).Take(limit).ToListAsync(ct);

        // PregnancyDto.nextContact needs the pregnancy's contacts.
        var pregnancyIds = pregnancies.Select(p => p.Id).ToList();
        var schedule = pregnancyIds.Count == 0
            ? []
            : await db.AncContacts.AsNoTracking().Where(c => pregnancyIds.Contains(c.PregnancyId)).ToListAsync(ct);

        var items = new List<TimelineItemDto>(pregnancies.Count + contacts.Count + deliveries.Count);
        foreach (var p in pregnancies)
        {
            var risk = p.RiskLevel == RiskLevel.High ? "high risk" : "normal risk";
            items.Add(new TimelineItemDto(TimelineKind.PregnancyRegistered, p.CreatedAt, "Pregnancy registered",
                $"EDD {p.Edd:yyyy-MM-dd} · G{p.Gravida} P{p.Para} · {risk}", null, p.Id,
                p.ToDto(schedule.Where(c => c.PregnancyId == p.Id).Ordered(), rules, today)));
        }

        foreach (var c in contacts)
        {
            // "BP 150/95 · Hb 9.2 · referred" (A.2 example)
            var parts = new List<string>();
            if (c.Findings?.BpSys is { } sys && c.Findings.BpDia is { } dia) parts.Add($"BP {sys}/{dia}");
            if (c.Findings?.HbGdl is { } hb) parts.Add($"Hb {hb}");
            if (c.Referral is not null) parts.Add("referred");
            var badge = c.TriageLevel switch
            {
                TriageLevel.Red => TimelineBadge.Red,
                TriageLevel.Amber => TimelineBadge.Amber,
                TriageLevel.Green => TimelineBadge.Green,
                _ => (TimelineBadge?)null,
            };
            items.Add(new TimelineItemDto(TimelineKind.AncContact, c.DoneAt!.Value, $"ANC contact {c.ContactNo} (week {c.WeekTarget})",
                parts.Count == 0 ? null : string.Join(" · ", parts), badge, c.Id, c.ToDto()));
        }

        foreach (var d in deliveries)
        {
            var title = d.Outcome == DeliveryOutcome.LiveBirth ? "Delivery — live birth" : "Delivery — stillbirth";
            var parts = new List<string> { PlaceLabels[d.Place], ModeLabels[d.Mode] };
            if (d.BabyWeightKg is { } kg) parts.Add($"{kg} kg");
            if (d.BabySex is { } sex) parts.Add(sex == BabySex.Female ? "girl" : "boy");
            items.Add(new TimelineItemDto(TimelineKind.Delivery, d.DeliveredAt, title, string.Join(" · ", parts), null, d.Id, d.ToDto()));
        }

        return items.OrderByDescending(i => i.At).Take(limit).ToList();
    }
}
