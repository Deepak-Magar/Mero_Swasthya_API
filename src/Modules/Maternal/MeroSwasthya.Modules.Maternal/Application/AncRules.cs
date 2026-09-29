using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>
/// The A.5 RULES table (contract 2026-09-18.1) as code. It must stay equivalent to the app's
/// <c>assets/rules.json</c>, which Catalog serves verbatim at GET /rules; the unit test
/// <c>RulesServiceTests.In_code_rules_equal_the_document_served_by_GET_rules</c> enforces that.
/// Changing the protocol means changing rules.json, this file and the version — together.
/// </summary>
internal static class AncRules
{
    public const string Version = "2026-09-18.1";

    /// <summary>Naegele's rule as A.2 states it: a flat 280 days.</summary>
    public const int GestationDays = 280;

    // Triage thresholds (A.5 "triage").
    public const int SevereBpSys = 160;
    public const int SevereBpDia = 110;
    public const int RaisedBpSys = 140;
    public const int RaisedBpDia = 90;
    public const double SevereAnaemiaHb = 7;
    public const double AnaemiaHb = 10;

    /// <summary>20 weeks: before that there is nothing to feel, so absent movement means nothing.</summary>
    public const int FetalMovementFromDays = 140;

    public const string SevereHeadacheCode = "SEVERE_HEADACHE_BLURRED_VISION";

    public static readonly IReadOnlyList<AncScheduleEntry> Schedule =
    [
        new(1, 12, ["weight", "bp", "hb", "urineProtein", "ifa", "td1", "riskAssessment"]),
        new(2, 20, ["weight", "bp", "fundalHeight", "ifa", "calcium", "deworming", "td2"]),
        new(3, 26, ["weight", "bp", "fundalHeight", "fhr", "ifa", "calcium"]),
        new(4, 30, ["weight", "bp", "fundalHeight", "fhr", "hb", "urineProtein", "ifa", "calcium", "birthPlan"]),
        new(5, 34, ["weight", "bp", "fundalHeight", "fhr", "ifa", "calcium", "birthPlan"]),
        new(6, 36, ["weight", "bp", "fundalHeight", "fhr", "urineProtein", "ifa", "calcium", "presentation"]),
        new(7, 38, ["weight", "bp", "fundalHeight", "fhr", "ifa", "calcium"]),
        new(8, 40, ["weight", "bp", "fundalHeight", "fhr", "ifa", "calcium", "labourSigns"]),
    ];

    public static readonly IReadOnlyList<DangerSignRule> DangerSigns =
    [
        new("VAGINAL_BLEEDING", DangerSignLevel.Red, "Vaginal bleeding", "योनिबाट रगत बग्नु"),
        new("CONVULSIONS", DangerSignLevel.Red, "Convulsions / fits", "काम्ने / मुर्छा पर्ने"),
        new(SevereHeadacheCode, DangerSignLevel.Red, "Severe headache with blurred vision", "कडा टाउको दुखाइ र आँखा धमिलो"),
        new("FEVER_WEAKNESS", DangerSignLevel.Red, "High fever with weakness", "उच्च ज्वरो र कमजोरी"),
        new("SEVERE_ABDOMINAL_PAIN", DangerSignLevel.Red, "Severe abdominal pain", "पेट कडा दुख्ने"),
        new("DIFFICULTY_BREATHING", DangerSignLevel.Red, "Fast or difficult breathing", "सास फेर्न गाह्रो"),
        new("WATER_BREAK_PRETERM", DangerSignLevel.Red, "Water breaking before 37 weeks", "३७ हप्ता अघि पानी फुट्नु"),
        new("REDUCED_FETAL_MOVEMENT", DangerSignLevel.Red, "Reduced or absent fetal movement (after 20 wk)", "बच्चा नचल्ने / कम चल्ने"),
        new("SWELLING_FACE_HANDS", DangerSignLevel.Amber, "Swelling of face and hands", "अनुहार र हात सुन्निनु"),
        new("PERSISTENT_VOMITING", DangerSignLevel.Amber, "Persistent vomiting", "लगातार बान्ता"),
    ];

    public static readonly IReadOnlyList<RiskFactorRule> RiskFactors =
    [
        new("AGE_LT_18", "Age under 18", "१८ वर्ष मुनि"),
        new("AGE_GT_35", "Age over 35", "३५ वर्ष माथि"),
        new("PREV_CS", "Previous caesarean section", "पहिले शल्यक्रिया"),
        new("PREV_STILLBIRTH", "Previous stillbirth / neonatal death", "पहिले मृत जन्म"),
        new("GRAND_MULTIPARA", "Para ≥ 5", "५ वा बढी सन्तान"),
        new("MULTIPLE_PREGNANCY", "Twins / multiple", "जुम्ल्याहा"),
        new("HEIGHT_LT_145", "Height under 145 cm", "उचाइ १४५ से.मि. मुनि"),
        new("CHRONIC_ILLNESS", "Chronic illness (diabetes, hypertension, heart, HIV)", "दीर्घ रोग"),
    ];
}

/// <summary>Stateless; registered as a singleton.</summary>
internal sealed class RulesService : IRulesService
{
    private static readonly Dictionary<string, DangerSignRule> DangerSignsByCode =
        AncRules.DangerSigns.ToDictionary(d => d.Code, StringComparer.Ordinal);

    private static readonly Dictionary<string, RiskFactorRule> RiskFactorsByCode =
        AncRules.RiskFactors.ToDictionary(r => r.Code, StringComparer.Ordinal);

    public string Version => AncRules.Version;
    public IReadOnlyList<AncScheduleEntry> Schedule => AncRules.Schedule;
    public IReadOnlyList<DangerSignRule> DangerSigns => AncRules.DangerSigns;
    public IReadOnlyList<RiskFactorRule> RiskFactors => AncRules.RiskFactors;

    public DangerSignRule? DangerSign(string code) => DangerSignsByCode.GetValueOrDefault(code);

    public RiskFactorRule? RiskFactor(string code) => RiskFactorsByCode.GetValueOrDefault(code);

    public DateOnly EddFromLmp(DateOnly lmp) => lmp.AddDays(AncRules.GestationDays);

    public DateOnly LmpFromEdd(DateOnly edd) => edd.AddDays(-AncRules.GestationDays);

    public int GestationalAgeDays(DateOnly edd, DateOnly today) => today.DayNumber - LmpFromEdd(edd).DayNumber;

    public IReadOnlyList<ScheduledContact> ScheduleFor(DateOnly lmp) =>
        AncRules.Schedule.Select(e => new ScheduledContact(e.ContactNo, e.WeekTarget, lmp.AddDays(e.WeekTarget * 7))).ToList();

    public RiskLevel RiskLevelFor(IEnumerable<string> riskFactors) => riskFactors.Any() ? RiskLevel.High : RiskLevel.Normal;

    public TriageResult Triage(TriageInput contact) => TriageEngine.Evaluate(contact, DangerSign);
}
