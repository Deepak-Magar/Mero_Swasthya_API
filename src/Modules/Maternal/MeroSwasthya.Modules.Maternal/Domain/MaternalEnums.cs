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

/// <summary>A.2 Delivery.place.</summary>
public enum DeliveryPlace
{
    [WireName("home")] Home,
    [WireName("birthing_centre")] BirthingCentre,
    [WireName("hospital")] Hospital,
    [WireName("on_the_way")] OnTheWay,
}

/// <summary>A.2 Delivery.mode.</summary>
public enum DeliveryMode
{
    [WireName("normal")] Normal,
    [WireName("assisted")] Assisted,
    [WireName("cs")] Cs,
}

/// <summary>A.2 Delivery.outcome.</summary>
public enum DeliveryOutcome
{
    [WireName("live_birth")] LiveBirth,
    [WireName("stillbirth")] Stillbirth,
}

/// <summary>A.2 Delivery.babySex.</summary>
public enum BabySex
{
    [WireName("female")] Female,
    [WireName("male")] Male,
}
