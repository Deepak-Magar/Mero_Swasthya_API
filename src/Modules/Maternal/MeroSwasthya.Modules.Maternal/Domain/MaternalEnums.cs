using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.Modules.Maternal.Domain;

/// <summary>A.2 AncContact.triageLevel / TimelineItem.badge.</summary>
public enum TriageLevel
{
    [WireName("green")] Green,
    [WireName("amber")] Amber,
    [WireName("red")] Red,
}

/// <summary>A.5 dangerSigns[].level.</summary>
public enum DangerSignLevel
{
    [WireName("red")] Red,
    [WireName("amber")] Amber,
}

/// <summary>A.2 Pregnancy.riskLevel — high if any risk factor is present.</summary>
public enum RiskLevel
{
    [WireName("normal")] Normal,
    [WireName("high")] High,
}

/// <summary>A.2 Pregnancy.status.</summary>
public enum PregnancyStatus
{
    [WireName("active")] Active,
    [WireName("delivered")] Delivered,
    [WireName("ended")] Ended,
}

/// <summary>A.2 AncContact.findings.urineProtein.</summary>
public enum UrineProtein
{
    [WireName("neg")] Neg,
    [WireName("trace")] Trace,
    [WireName("+")] One,
    [WireName("++")] Two,
    [WireName("+++")] Three,
}

/// <summary>A.2 AncContact.findings.fetalMovement.</summary>
public enum FetalMovement
{
    [WireName("normal")] Normal,
    [WireName("reduced")] Reduced,
    [WireName("absent")] Absent,
}

internal static class MaternalEnums
{
    /// <summary>A.5 triage: proteinuria means one of "+", "++", "+++".</summary>
    public static bool IsProteinuria(this UrineProtein? value) =>
        value is UrineProtein.One or UrineProtein.Two or UrineProtein.Three;
}
