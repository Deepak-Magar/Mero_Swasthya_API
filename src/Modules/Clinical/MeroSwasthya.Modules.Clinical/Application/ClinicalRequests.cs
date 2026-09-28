using FluentValidation;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.Modules.Clinical.Application;

/// <summary>A.4 POST /patients/:id/visits body.</summary>
internal sealed class CreateVisitRequest
{
    public string? Id { get; init; }
    public DateTime? VisitAt { get; init; }
    public string? ChiefComplaintCode { get; init; }
    public VitalsDto? Vitals { get; init; }
    public List<string>? DiagnosisCodes { get; init; }
    public string? Notes { get; init; }
    public string? Advice { get; init; }
    public DateOnly? FollowUpAt { get; init; }
    public ReferralRequest? Referral { get; init; }
    public List<PrescriptionRequest>? Prescriptions { get; init; }
    public string? SupersedesId { get; init; }
}

internal sealed class ReferralRequest
{
    public string? FacilityId { get; init; }
    public string? FacilityName { get; init; }
    public string? Reason { get; init; }
    public string? Urgency { get; init; }
}

/// <summary>A.4: <c>drugName</c> is optional on create — the server fills it from the codelist.</summary>
internal sealed class PrescriptionRequest
{
    public string? Id { get; init; }
    public string? DrugCode { get; init; }
    public string? DrugName { get; init; }
    public string? Dose { get; init; }
    public string? Frequency { get; init; }
    public int? DurationDays { get; init; }
    public string? InstructionsNp { get; init; }
}

/// <summary>A.4 POST /documents/presign body.</summary>
internal sealed class PresignDocumentRequest
{
    public string? Id { get; init; }
    public string? PatientId { get; init; }
    public string? Type { get; init; }
    public string? Title { get; init; }
    public DateOnly? TakenAt { get; init; }
    public string? ContentType { get; init; }
    public long? SizeBytes { get; init; }
}

internal sealed record VisitResponse(VisitDto Visit);

internal sealed record DocumentResponse(DocumentDto Document);

/// <summary>A.4 presign response.</summary>
internal sealed record PresignResponse(
    DocumentDto Document,
    string UploadUrl,
    string UploadMethod,
    IReadOnlyDictionary<string, string> UploadHeaders,
    int ExpiresInSec);

internal sealed class CreateVisitRequestValidator : AbstractValidator<CreateVisitRequest>
{
    public const int MaxPrescriptions = 30;

    public CreateVisitRequestValidator(ICodeListLookup codes, IClock clock)
    {
        RuleFor(x => x.Id).NotEmpty().ClientId();
        RuleFor(x => x.VisitAt).NotNull()
            .Must(v => v is null || v.Value <= clock.UtcNow.AddDays(1)).WithMessage("'visitAt' cannot be in the future");
        RuleFor(x => x.ChiefComplaintCode).NotEmpty()
            .MustAsync(async (c, ct) => await Known(codes, CodeListKind.Complaint, c!, ct))
            .WithMessage(x => $"Unknown complaint code \"{x.ChiefComplaintCode}\"");
        RuleFor(x => x.DiagnosisCodes)
            .Must(l => l is null || l.Count <= 20).WithMessage("At most 20 diagnosis codes")
            .MustAsync(async (l, ct) => l is null || await AllKnown(codes, CodeListKind.Diagnosis, l, ct))
            .WithMessage(x => $"Unknown diagnosis code(s): {string.Join(", ", x.DiagnosisCodes ?? [])}");

        RuleFor(x => x.Vitals!.BpSys).InclusiveBetween(50, 300).When(x => x.Vitals?.BpSys is not null).OverridePropertyName("vitals.bpSys");
        RuleFor(x => x.Vitals!.BpDia).InclusiveBetween(20, 200).When(x => x.Vitals?.BpDia is not null).OverridePropertyName("vitals.bpDia");
        RuleFor(x => x.Vitals!.Pulse).InclusiveBetween(20, 250).When(x => x.Vitals?.Pulse is not null).OverridePropertyName("vitals.pulse");
        RuleFor(x => x.Vitals!.TempC).InclusiveBetween(30, 45).When(x => x.Vitals?.TempC is not null).OverridePropertyName("vitals.tempC");
        RuleFor(x => x.Vitals!.WeightKg).InclusiveBetween(0.3, 400).When(x => x.Vitals?.WeightKg is not null).OverridePropertyName("vitals.weightKg");
        RuleFor(x => x.Vitals!.Spo2).InclusiveBetween(50, 100).When(x => x.Vitals?.Spo2 is not null).OverridePropertyName("vitals.spo2");

        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Advice).MaximumLength(1000);
        RuleFor(x => x.FollowUpAt)
            .Must((x, f) => f is null || x.VisitAt is null || f.Value >= DateOnly.FromDateTime(x.VisitAt.Value))
            .WithMessage("'followUpAt' cannot be before the visit");
        RuleFor(x => x.SupersedesId).ClientId().When(x => x.SupersedesId is not null);

        RuleFor(x => x.Referral!).ChildRules(r =>
        {
            r.RuleFor(x => x.FacilityName).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
            r.RuleFor(x => x.Urgency).NotEmpty().WireEnum<ReferralRequest, ReferralUrgency>();
            r.RuleFor(x => x.FacilityId).MaximumLength(64);
        }).When(x => x.Referral is not null).OverridePropertyName("referral");

        RuleFor(x => x.Prescriptions)
            .Must(l => l is null || l.Count <= MaxPrescriptions).WithMessage($"At most {MaxPrescriptions} prescriptions")
            .Must(l => l is null || l.Select(p => p?.Id).Distinct().Count() == l.Count).WithMessage("Prescription ids must be unique");
        RuleForEach(x => x.Prescriptions).ChildRules(p =>
        {
            p.RuleFor(x => x.Id).NotEmpty().ClientId();
            p.RuleFor(x => x.DrugCode).NotEmpty()
                .MustAsync(async (c, ct) => await Known(codes, CodeListKind.Drug, c!, ct))
                .WithMessage(x => $"Unknown drug code \"{x.DrugCode}\"");
            p.RuleFor(x => x.DrugName).MaximumLength(200);
            p.RuleFor(x => x.Dose).NotEmpty().MaximumLength(50);
            p.RuleFor(x => x.Frequency).NotEmpty().WireEnum<PrescriptionRequest, PrescriptionFrequency>();
            p.RuleFor(x => x.DurationDays).NotNull().InclusiveBetween(1, 365);
            p.RuleFor(x => x.InstructionsNp).MaximumLength(300);
        });
    }

    private static async Task<bool> Known(ICodeListLookup codes, CodeListKind kind, string code, CancellationToken ct) =>
        (await codes.LabelsAsync(kind, [code], ct)).ContainsKey(code);

    private static async Task<bool> AllKnown(ICodeListLookup codes, CodeListKind kind, List<string> list, CancellationToken ct)
    {
        var labels = await codes.LabelsAsync(kind, list, ct);
        return list.All(labels.ContainsKey);
    }
}

internal sealed class PresignDocumentRequestValidator : AbstractValidator<PresignDocumentRequest>
{
    public PresignDocumentRequestValidator(IClock clock)
    {
        RuleFor(x => x.Id).NotEmpty().ClientId();
        RuleFor(x => x.PatientId).NotEmpty().ClientId();
        RuleFor(x => x.Type).NotEmpty().WireEnum<PresignDocumentRequest, DocumentType>();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TakenAt).NotNull()
            .Must(d => d is null || d.Value <= clock.TodayUtc.AddDays(1)).WithMessage("'takenAt' cannot be in the future");
        RuleFor(x => x.ContentType).NotEmpty()
            .Must(c => StorageOptions.ContentTypes.Contains(c)).WithMessage("Must be image/jpeg or image/png");
        RuleFor(x => x.SizeBytes).NotNull()
            .InclusiveBetween(1, StorageOptions.MaxBytes).WithMessage("Documents must be at most 2 MB");
    }
}
