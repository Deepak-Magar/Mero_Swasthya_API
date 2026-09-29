using System.Text.Json.Serialization;
using MeroSwasthya.Modules.Maternal.Domain;

namespace MeroSwasthya.Modules.Maternal.Contracts;

/// <summary>
/// A.2 AncContact.findings — every key optional, omitted when not measured (as Visit.vitals);
/// <c>notesText</c> is the addendum §5 free-text note, stored inside findings.
/// </summary>
public sealed record FindingsDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? WeightKg { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BpSys { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BpDia { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? FundalHeightCm { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FhrBpm { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? HbGdl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public UrineProtein? UrineProtein { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? TdDoseGiven { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? IfaGiven { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DewormingGiven { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? CalciumGiven { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FetalMovement? FetalMovement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NotesText { get; init; }
}

/// <summary>
/// One line of the "why" behind a triage level: a stable code plus the en/np texts from the rule
/// table. <c>AncContact.triageReasons</c> on the wire carries the English texts, as the app does.
/// </summary>
public sealed record TriageReason(string Code, string En, string Np);

/// <summary>A.5 triage output.</summary>
public sealed record TriageResult(TriageLevel Level, IReadOnlyList<TriageReason> Reasons)
{
    public IReadOnlyList<string> ReasonsEn => Reasons.Select(r => r.En).ToList();

    public IReadOnlyList<string> ReasonsNp => Reasons.Select(r => r.Np).ToList();

    public bool IsRed => Level == TriageLevel.Red;

    public bool IsAmber => Level == TriageLevel.Amber;
}

/// <summary>Everything A.5 triage looks at: the contact's findings and ticked signs, plus the pregnancy's risk level and age.</summary>
public sealed record TriageInput(
    FindingsDto? Findings,
    IReadOnlyList<string> DangerSigns,
    RiskLevel RiskLevel,
    int GestationalAgeDays);
