using MeroSwasthya.Modules.Maternal.Domain;

namespace MeroSwasthya.Modules.Maternal.Contracts;

/// <summary>A.5 ancSchedule[] entry.</summary>
public sealed record AncScheduleEntry(int ContactNo, int WeekTarget, IReadOnlyList<string> Checklist);

/// <summary>A.5 dangerSigns[] entry.</summary>
public sealed record DangerSignRule(string Code, DangerSignLevel Level, string En, string Np)
{
    public bool IsRed => Level == DangerSignLevel.Red;
}

/// <summary>A.5 riskFactors[] entry.</summary>
public sealed record RiskFactorRule(string Code, string En, string Np);

/// <summary>One of the eight contacts of a pregnancy as the schedule places it: <c>dueAt = lmp + weekTarget × 7</c>.</summary>
public sealed record ScheduledContact(int ContactNo, int WeekTarget, DateOnly DueAt);

/// <summary>
/// The A.5 RULES table as versioned in-code data, plus the derived rules (EDD, schedule, risk level).
/// The version is the one GET /rules serves; a unit test keeps the two copies identical.
/// </summary>
public interface IRulesService
{
    string Version { get; }

    IReadOnlyList<AncScheduleEntry> Schedule { get; }

    IReadOnlyList<DangerSignRule> DangerSigns { get; }

    IReadOnlyList<RiskFactorRule> RiskFactors { get; }

    DangerSignRule? DangerSign(string code);

    RiskFactorRule? RiskFactor(string code);

    /// <summary>A.2 Pregnancy.edd: <c>lmp + 280 days</c>.</summary>
    DateOnly EddFromLmp(DateOnly lmp);

    /// <summary>A.6 #2: <c>edd − 280 days</c>, the anchor for scheduling when only the EDD is known.</summary>
    DateOnly LmpFromEdd(DateOnly edd);

    /// <summary>A.2 Pregnancy.gestationalAgeDays: <c>today − (edd − 280 d)</c>. Negative before the LMP; keeps counting past the EDD.</summary>
    int GestationalAgeDays(DateOnly edd, DateOnly today);

    /// <summary>The eight contacts for a pregnancy whose LMP (recorded or derived) is <paramref name="lmp"/>.</summary>
    IReadOnlyList<ScheduledContact> ScheduleFor(DateOnly lmp);

    /// <summary>A.2: high if any risk factor is present.</summary>
    RiskLevel RiskLevelFor(IEnumerable<string> riskFactors);
}
