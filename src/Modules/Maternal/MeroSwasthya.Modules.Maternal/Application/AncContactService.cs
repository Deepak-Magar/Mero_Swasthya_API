using FluentValidation;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Modules.Maternal.Infrastructure;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Events;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>A.4 PUT /pregnancies/:id/contacts/:contactNo body. <c>doneAt</c> defaults to now.</summary>
internal sealed class RecordContactRequest
{
    public DateTime? DoneAt { get; init; }
    public FindingsDto? Findings { get; init; }
    public List<string>? DangerSigns { get; init; }
    public ContactReferralRequest? Referral { get; init; }
}

internal sealed class ContactReferralRequest
{
    public string? FacilityId { get; init; }
    public string? FacilityName { get; init; }
    public string? Reason { get; init; }
    public string? Urgency { get; init; }
}

/// <summary>A.4: the recorded contact with the server's triage, and where to send her when it is not green.</summary>
internal sealed record ContactRecordedResponse(AncContactDto AncContact, FacilityDto? NearestReferral);

internal sealed class RecordContactRequestValidator : AbstractValidator<RecordContactRequest>
{
    public const int MaxDangerSigns = 20;

    public RecordContactRequestValidator(IRulesService rules, IClock clock)
    {
        RuleFor(x => x.DoneAt)
            .Must(d => d is null || d.Value <= clock.UtcNow.AddDays(1)).WithMessage("'doneAt' cannot be in the future");

        RuleFor(x => x.Findings!.WeightKg).InclusiveBetween(20, 200).When(x => x.Findings?.WeightKg is not null).OverridePropertyName("findings.weightKg");
        RuleFor(x => x.Findings!.BpSys).InclusiveBetween(50, 300).When(x => x.Findings?.BpSys is not null).OverridePropertyName("findings.bpSys");
        RuleFor(x => x.Findings!.BpDia).InclusiveBetween(20, 200).When(x => x.Findings?.BpDia is not null).OverridePropertyName("findings.bpDia");
        RuleFor(x => x.Findings!.FundalHeightCm).InclusiveBetween(5, 60).When(x => x.Findings?.FundalHeightCm is not null).OverridePropertyName("findings.fundalHeightCm");
        RuleFor(x => x.Findings!.FhrBpm).InclusiveBetween(40, 250).When(x => x.Findings?.FhrBpm is not null).OverridePropertyName("findings.fhrBpm");
        RuleFor(x => x.Findings!.HbGdl).InclusiveBetween(2, 25).When(x => x.Findings?.HbGdl is not null).OverridePropertyName("findings.hbGdl");
        RuleFor(x => x.Findings!.NotesText).MaximumLength(1000).When(x => x.Findings?.NotesText is not null).OverridePropertyName("findings.notesText");

        RuleFor(x => x.DangerSigns)
            .Must(l => l is null || l.Count <= MaxDangerSigns).WithMessage($"At most {MaxDangerSigns} danger signs")
            .Must(l => l is null || l.All(c => c is not null && rules.DangerSign(c) is not null))
            .WithMessage(x => $"Unknown danger sign code(s): {string.Join(", ", (x.DangerSigns ?? []).Where(c => c is null || rules.DangerSign(c) is null).Select(c => c ?? "null"))}");

        RuleFor(x => x.Referral!).ChildRules(r =>
        {
            r.RuleFor(x => x.FacilityName).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            r.RuleFor(x => x.Urgency).NotEmpty().WireEnum<ContactReferralRequest, ReferralUrgency>();
            r.RuleFor(x => x.FacilityId).MaximumLength(64);
        }).When(x => x.Referral is not null).OverridePropertyName("referral");
    }
}

/// <summary>A.4 "Maternal" — ANC contacts: list the schedule, record one (server-side triage).</summary>
internal sealed class AncContactService(
    MaternalDbContext db,
    PregnancyService pregnancies,
    MaternalAccess access,
    ICurrentUser currentUser,
    IRulesService rules,
    IFacilityDirectory facilities,
    IDomainEventPublisher events,
    IClock clock)
{
    public const int NearestCandidates = 3;

    /// <summary>Additive (not in A.4): the eight contacts of a pregnancy, by contactNo.</summary>
    public async Task<IReadOnlyList<AncContactDto>> ListAsync(string pregnancyId, CancellationToken ct)
    {
        var pregnancy = await pregnancies.LoadAsync(pregnancyId, ct);
        await access.ReadAsync(pregnancy.PatientId, ct);
        return (await pregnancies.ContactsAsync(pregnancyId, ct)).Select(c => c.ToDto()).ToList();
    }

    /// <summary>
    /// Records (or re-records) contact <paramref name="contactNo"/>. The server's triage is what the
    /// app must display (A.4); <c>nearestReferral</c> is filled when the level is red or amber.
    /// </summary>
    public async Task<ContactRecordedResponse> RecordAsync(string pregnancyId, string contactNo, RecordContactRequest request, CancellationToken ct)
    {
        var pregnancy = await pregnancies.LoadAsync(pregnancyId, ct);
        await access.AppendAsync(pregnancy.PatientId, ct);
        var user = await currentUser.GetAsync(ct);

        // A.3: contactNo outside 1..8 is a rule violation, not a routing miss.
        if (!int.TryParse(contactNo, out var no) || no < 1 || no > rules.Schedule.Count)
            throw AppException.RuleViolation($"contactNo must be between 1 and {rules.Schedule.Count}", new { contactNo });
        if (pregnancy.Status != PregnancyStatus.Active)
            throw AppException.RuleViolation($"Pregnancy is {pregnancy.Status.ToWire()}; contacts can only be recorded on an active pregnancy");

        var contact = await db.AncContacts.FirstOrDefaultAsync(c => c.PregnancyId == pregnancyId && c.ContactNo == no, ct)
                      ?? throw AppException.NotFound("ANC contact");

        var now = clock.UtcNow;
        var doneAt = request.DoneAt ?? now;
        var dangerSigns = (request.DangerSigns ?? []).Distinct(StringComparer.Ordinal).ToList();
        var findings = request.Findings is null ? null : request.Findings with { NotesText = Blank(request.Findings.NotesText) };

        // Gestational age on the day of the contact (the app evaluates at "now", which is the same day).
        var triage = rules.Triage(new TriageInput(
            findings, dangerSigns, pregnancy.RiskLevel,
            rules.GestationalAgeDays(pregnancy.Edd, DateOnly.FromDateTime(doneAt))));

        contact.DoneAt = doneAt;
        contact.ProviderUserId = user.Id;
        contact.Findings = findings;
        contact.DangerSigns = dangerSigns;
        contact.TriageLevel = triage.Level;
        contact.TriageReasons = triage.ReasonsEn.ToList();
        contact.Referral = request.Referral is null
            ? null
            : new ReferralDto(Blank(request.Referral.FacilityId), request.Referral.FacilityName!.Trim(),
                request.Referral.Reason!.Trim(), WireEnum.Parse<ReferralUrgency>(request.Referral.Urgency!));
        contact.Version++;
        contact.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await access.WroteAsync(pregnancy.PatientId, ct); // A.4: "Writes AuditEntry contact_recorded."
        // A.4: "Cancels pending anc_missed reminder for this contact."
        await events.PublishAsync(new AncContactRecorded(pregnancy.Id, pregnancy.PatientId, contact.Id, contact.ContactNo), ct);

        var nearest = triage.Level == TriageLevel.Green ? null : await NearestReferralAsync(user, pregnancy, ct);
        return new ContactRecordedResponse(contact.ToDto(), nearest);
    }

    /// <summary>
    /// A.5 action: "nearest facility with birthing centre / hospital". Measured from the health worker's
    /// own facility (excluding it — a referral goes elsewhere); for an owner recording at home, the birth
    /// plan's facility; otherwise null.
    /// </summary>
    private async Task<FacilityDto?> NearestReferralAsync(CurrentUserInfo user, Pregnancy pregnancy, CancellationToken ct)
    {
        if (user.FacilityId is not null && await facilities.FindAsync(user.FacilityId, ct) is { } origin)
        {
            var candidates = await facilities.NearestAsync(origin.Lat, origin.Lng, birthingOnly: true, NearestCandidates, ct);
            var best = candidates.FirstOrDefault(c => c.Facility.Id != origin.Id);
            if (best.Facility is not null) return FacilityDto.From(best.Facility, Math.Round(best.DistanceKm, 1));
        }

        if (pregnancy.BirthPlan?.FacilityId is { } planned && await facilities.FindAsync(planned, ct) is { } facility)
            return FacilityDto.From(facility);

        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
