using FluentValidation;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>A.4 POST /patients/:id/pregnancies body. Omitted gravida/para default to 1/0; riskFactors to [].</summary>
internal sealed class CreatePregnancyRequest
{
    public string? Id { get; init; }
    public DateOnly? Lmp { get; init; }
    public DateOnly? Edd { get; init; }
    public int? Gravida { get; init; }
    public int? Para { get; init; }
    public List<string>? RiskFactors { get; init; }
    public BirthPlanRequest? BirthPlan { get; init; }
}

internal sealed class BirthPlanRequest
{
    public string? FacilityId { get; init; }
    public string? FacilityName { get; init; }
    public string? Transport { get; init; }
    public bool? MoneySaved { get; init; }
    public string? BloodDonorName { get; init; }
    public string? BloodDonorPhone { get; init; }
    public string? CompanionName { get; init; }
}

/// <summary>A.4 PATCH /pregnancies/:id body: birthPlan / riskFactors / status=ended; <c>version</c> required.</summary>
internal sealed class PatchPregnancyRequest
{
    public int? Version { get; init; }
    public Optional<BirthPlanRequest> BirthPlan { get; init; }
    public Optional<List<string>> RiskFactors { get; init; }
    public Optional<string> Status { get; init; }
}

internal sealed record PregnancyCreatedResponse(PregnancyDto Pregnancy, IReadOnlyList<AncContactDto> AncContacts);

internal sealed record PregnancyResponse(PregnancyDto Pregnancy);

/// <summary>A.4 GET /pregnancies/:id — reminders arrive with the Reminders module.</summary>
internal sealed record PregnancyBundleResponse(
    PregnancyDto Pregnancy,
    IReadOnlyList<AncContactDto> AncContacts,
    DeliveryDto? Delivery,
    IReadOnlyList<object> Reminders);

internal static class PregnancyRules
{
    public const int MaxGravida = 25;
    public const int MaxRiskFactors = 20;

    /// <summary>An LMP more than this far back is not a current pregnancy.</summary>
    public const int MaxLmpAgeDays = 320;

    public static void RiskFactors<T>(IRuleBuilder<T, List<string>?> rule, IRulesService rules) =>
        rule.Must(l => l is null || l.Count <= MaxRiskFactors).WithMessage($"At most {MaxRiskFactors} risk factors")
            .Must(l => l is null || l.All(c => c is not null && rules.RiskFactor(c) is not null))
            .WithMessage(x => $"Unknown risk factor code(s): {string.Join(", ", Unknown(x, rules))}");

    private static IEnumerable<string> Unknown<T>(T request, IRulesService rules)
    {
        var list = request switch
        {
            CreatePregnancyRequest c => c.RiskFactors,
            PatchPregnancyRequest p => p.RiskFactors.Value,
            _ => null,
        };
        return (list ?? []).Where(c => c is null || rules.RiskFactor(c) is null).Select(c => c ?? "null");
    }

    public static void BirthPlan(InlineValidator<BirthPlanRequest> b)
    {
        b.RuleFor(x => x.FacilityId).MaximumLength(64);
        b.RuleFor(x => x.FacilityName).MaximumLength(200);
        b.RuleFor(x => x.Transport).MaximumLength(200);
        b.RuleFor(x => x.BloodDonorName).MaximumLength(100);
        b.RuleFor(x => x.BloodDonorPhone).E164();
        b.RuleFor(x => x.CompanionName).MaximumLength(100);
    }
}

internal sealed class CreatePregnancyRequestValidator : AbstractValidator<CreatePregnancyRequest>
{
    public CreatePregnancyRequestValidator(IRulesService rules, IClock clock)
    {
        var today = clock.TodayUtc;
        RuleFor(x => x.Id).NotEmpty().ClientId();
        RuleFor(x => x.Lmp)
            .Must((x, lmp) => lmp is not null || x.Edd is not null).WithMessage("Either 'lmp' or 'edd' is required")
            .Must(lmp => lmp is null || lmp.Value <= today).WithMessage("'lmp' cannot be in the future")
            .Must(lmp => lmp is null || lmp.Value >= today.AddDays(-PregnancyRules.MaxLmpAgeDays))
            .WithMessage($"'lmp' cannot be more than {PregnancyRules.MaxLmpAgeDays} days ago");
        RuleFor(x => x.Edd)
            .Must(edd => edd is null || edd.Value <= today.AddDays(300)).WithMessage("'edd' cannot be more than 300 days away")
            .Must(edd => edd is null || edd.Value >= today.AddDays(-60)).WithMessage("'edd' cannot be more than 60 days ago")
            .When(x => x.Edd is not null);
        RuleFor(x => x.Gravida).InclusiveBetween(1, PregnancyRules.MaxGravida).When(x => x.Gravida is not null);
        RuleFor(x => x.Para)
            .InclusiveBetween(0, PregnancyRules.MaxGravida)
            .Must((x, para) => para is null || para.Value < (x.Gravida ?? 1)).WithMessage("'para' must be less than 'gravida'")
            .When(x => x.Para is not null);
        PregnancyRules.RiskFactors(RuleFor(x => x.RiskFactors), rules);
        RuleFor(x => x.BirthPlan!).ChildRules(PregnancyRules.BirthPlan).When(x => x.BirthPlan is not null).OverridePropertyName("birthPlan");
    }
}

internal sealed class PatchPregnancyRequestValidator : AbstractValidator<PatchPregnancyRequest>
{
    /// <summary>A rule on <c>Optional&lt;T&gt;.Value</c> reported under the JSON field name.</summary>
    private IRuleBuilderInitial<PatchPregnancyRequest, TProperty> Named<TProperty>(
        System.Linq.Expressions.Expression<Func<PatchPregnancyRequest, TProperty>> expression, string field) =>
        RuleFor(expression).Configure(rule => rule.PropertyName = field);

    public PatchPregnancyRequestValidator(IRulesService rules)
    {
        RuleFor(x => x.Version).NotNull().WithMessage("'version' is required").GreaterThanOrEqualTo(1);
        When(x => x.RiskFactors.HasValue, () =>
        {
            Named(x => x.RiskFactors.Value, "riskFactors").NotNull().WithMessage("Use [] to clear the list");
            PregnancyRules.RiskFactors(Named(x => x.RiskFactors.Value, "riskFactors"), rules);
        });
        When(x => x.BirthPlan.HasValue && x.BirthPlan.Value is not null, () =>
            Named(x => x.BirthPlan.Value!, "birthPlan").ChildRules(PregnancyRules.BirthPlan));
        When(x => x.Status.HasValue, () =>
            Named(x => x.Status.Value, "status")
                .Must(s => s == PregnancyStatus.Ended.ToWire())
                .WithMessage("Only \"ended\" can be set here; \"delivered\" comes from POST /pregnancies/:id/delivery"));
    }
}
