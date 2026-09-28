using FluentValidation;
using MeroSwasthya.Modules.Grants.Contracts;
using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.Modules.Grants.Application;

/// <summary>A.4 POST /grants (+ addendum §4 <c>sections</c>).</summary>
internal sealed class CreateGrantRequest
{
    public string? PatientId { get; init; }
    public string? Scope { get; init; }
    public int? TtlMinutes { get; init; }
    public List<string>? Sections { get; init; }
}

/// <summary>A.4 POST /grants/redeem. <c>pin</c> is only needed for a long-lived (printed card) grant.</summary>
internal sealed class RedeemGrantRequest
{
    public string? QrPayload { get; init; }
    public string? Pin { get; init; }
}

internal sealed record CreateGrantResponse(GrantDto Grant, string Token, string QrPayload);

internal sealed record GrantResponse(GrantDto Grant);

/// <summary>A.4 redeem bundle.</summary>
internal sealed record RedeemResponse(
    GrantDto Grant,
    PatientDto Patient,
    PatientSummaryDto Summary,
    IReadOnlyList<TimelineItemDto> Timeline,
    object? Pregnancy,
    IReadOnlyList<object> AncContacts);

internal static class GrantLimits
{
    public const int DefaultTtlMinutes = 10;
    public const int MaxTtlMinutes = 525_600;   // A.7 printed card: one year
    public const int LongLivedFromMinutes = 1_440; // a day or more = a card that outlives the consultation
    public const int PerPatientPerHour = 20;
    public static readonly TimeSpan AccessWindow = TimeSpan.FromHours(24);
}

internal sealed class CreateGrantRequestValidator : AbstractValidator<CreateGrantRequest>
{
    public CreateGrantRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty().ClientId();
        RuleFor(x => x.Scope).WireEnum<CreateGrantRequest, GrantScope>();
        RuleFor(x => x.TtlMinutes).InclusiveBetween(1, GrantLimits.MaxTtlMinutes).When(x => x.TtlMinutes is not null);
        RuleFor(x => x.Scope)
            .Must(s => s is null || s == "read")
            .When(x => x.TtlMinutes >= GrantLimits.LongLivedFromMinutes)
            .WithMessage($"A long-lived grant (ttlMinutes ≥ {GrantLimits.LongLivedFromMinutes}) must have scope \"read\"");
        RuleFor(x => x.Sections)
            .Must(l => l is null || l.All(s => Shared.Json.WireEnum.TryParse<GrantSection>(s, out _)))
            .WithMessage($"Items must be one of: {string.Join(", ", Shared.Json.WireEnum.Names<GrantSection>())}");
    }
}

internal sealed class RedeemGrantRequestValidator : AbstractValidator<RedeemGrantRequest>
{
    public RedeemGrantRequestValidator()
    {
        RuleFor(x => x.QrPayload).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Pin).Matches("^[0-9]{4}$").When(x => !string.IsNullOrEmpty(x.Pin)).WithMessage("PIN must be 4 digits");
    }
}
