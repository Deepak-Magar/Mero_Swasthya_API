using FluentValidation;
using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.Modules.Patients.Application;

/// <summary>A.4 POST /patients body. Omitted optional fields take their defaults (A.1).</summary>
internal sealed class CreatePatientRequest
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Sex { get; init; }
    public DateOnly? Dob { get; init; }
    public string? BloodGroup { get; init; }
    public int? Ward { get; init; }
    public string? Municipality { get; init; }
    public List<string>? Allergies { get; init; }
    public List<string>? ChronicConditions { get; init; }
    public string? EmergencyContactPhone { get; init; }
}

/// <summary>A.4 PATCH /patients/:id body. <c>version</c> is required; omitted fields are unchanged (A.1).</summary>
internal sealed class PatchPatientRequest
{
    public int? Version { get; init; }
    public Optional<string> Name { get; init; }
    public Optional<string> Sex { get; init; }
    public Optional<DateOnly?> Dob { get; init; }
    public Optional<string> BloodGroup { get; init; }
    public Optional<int?> Ward { get; init; }
    public Optional<string> Municipality { get; init; }
    public Optional<List<string>> Allergies { get; init; }
    public Optional<List<string>> ChronicConditions { get; init; }
    public Optional<string> EmergencyContactPhone { get; init; }
}

internal static class PatientRules
{
    public static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
    public const int MaxListItems = 50;
    public const int MaxWard = 40;

    public static void Name<T>(IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().Must(n => n is not null && n.Trim().Length > 0).WithMessage("'name' must not be blank").MaximumLength(100);

    public static void Dob<T>(IRuleBuilder<T, DateOnly?> rule, IClock clock) =>
        rule.NotNull()
            .Must(d => d is null || d.Value <= clock.TodayUtc).WithMessage("'dob' cannot be in the future")
            .Must(d => d is null || d.Value.Year >= 1900).WithMessage("'dob' must be after 1900-01-01");

    public static void BloodGroup<T>(IRuleBuilder<T, string?> rule) =>
        rule.Must(b => b is null || BloodGroups.Contains(b)).WithMessage($"Must be one of: {string.Join(", ", BloodGroups)}, or null");

    public static void Ward<T>(IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(1, MaxWard).When((_, w) => w is not null);

    public static void Allergies<T>(IRuleBuilder<T, List<string>?> rule) =>
        rule.Must(l => l is null || l.Count <= MaxListItems).WithMessage($"At most {MaxListItems} items")
            .Must(l => l is null || l.All(a => !string.IsNullOrWhiteSpace(a) && a.Length <= 100))
            .WithMessage("Items must be non-blank text of at most 100 characters");

    /// <summary>Diagnosis codes (codelist kind=diagnosis), e.g. "E11". Format-checked; unknown codes keep their code as label.</summary>
    public static void ChronicConditions<T>(IRuleBuilder<T, List<string>?> rule) =>
        rule.Must(l => l is null || l.Count <= MaxListItems).WithMessage($"At most {MaxListItems} items")
            .Must(l => l is null || l.All(c => c is not null && System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Za-z0-9_.-]{1,20}$")))
            .WithMessage("Items must be diagnosis codes such as \"E11\"");
}

internal sealed class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientRequestValidator(IClock clock)
    {
        RuleFor(x => x.Id).NotEmpty().ClientId();
        PatientRules.Name(RuleFor(x => x.Name));
        RuleFor(x => x.Sex).NotEmpty().WireEnum<CreatePatientRequest, Sex>();
        PatientRules.Dob(RuleFor(x => x.Dob), clock);
        PatientRules.BloodGroup(RuleFor(x => x.BloodGroup));
        PatientRules.Ward(RuleFor(x => x.Ward));
        RuleFor(x => x.Municipality).MaximumLength(100);
        PatientRules.Allergies(RuleFor(x => x.Allergies));
        PatientRules.ChronicConditions(RuleFor(x => x.ChronicConditions));
        RuleFor(x => x.EmergencyContactPhone).E164();
    }
}

internal sealed class PatchPatientRequestValidator : AbstractValidator<PatchPatientRequest>
{
    /// <summary>A rule on <c>Optional&lt;T&gt;.Value</c> reported under the JSON field name.</summary>
    private IRuleBuilderInitial<PatchPatientRequest, TProperty> Named<TProperty>(
        System.Linq.Expressions.Expression<Func<PatchPatientRequest, TProperty>> expression, string field) =>
        RuleFor(expression).Configure(rule => rule.PropertyName = field);

    public PatchPatientRequestValidator(IClock clock)
    {
        RuleFor(x => x.Version).NotNull().WithMessage("'version' is required").GreaterThanOrEqualTo(1);

        When(x => x.Name.HasValue, () => PatientRules.Name(Named(x => x.Name.Value, "name")));
        When(x => x.Sex.HasValue, () =>
            Named(x => x.Sex.Value, "sex").NotEmpty().WireEnum<PatchPatientRequest, Sex>());
        When(x => x.Dob.HasValue, () => PatientRules.Dob(Named(x => x.Dob.Value, "dob"), clock));
        When(x => x.BloodGroup.HasValue, () =>
            PatientRules.BloodGroup(Named(x => x.BloodGroup.Value, "bloodGroup")));
        When(x => x.Ward.HasValue, () => PatientRules.Ward(Named(x => x.Ward.Value, "ward")));
        When(x => x.Municipality.HasValue, () =>
            Named(x => x.Municipality.Value, "municipality").MaximumLength(100));
        When(x => x.Allergies.HasValue, () =>
        {
            Named(x => x.Allergies.Value, "allergies").NotNull().WithMessage("Use [] to clear the list");
            PatientRules.Allergies(Named(x => x.Allergies.Value, "allergies"));
        });
        When(x => x.ChronicConditions.HasValue, () =>
        {
            Named(x => x.ChronicConditions.Value, "chronicConditions").NotNull().WithMessage("Use [] to clear the list")
                ;
            PatientRules.ChronicConditions(Named(x => x.ChronicConditions.Value, "chronicConditions"));
        });
        When(x => x.EmergencyContactPhone.HasValue, () =>
            Named(x => x.EmergencyContactPhone.Value, "emergencyContactPhone").E164());
    }
}
