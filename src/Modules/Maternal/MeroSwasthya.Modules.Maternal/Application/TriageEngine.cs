using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;

namespace MeroSwasthya.Modules.Maternal.Application;

/// <summary>
/// A.5 "triage", evaluated exactly as the app's <c>lib/domain/rules/triage.dart</c>: same rule
/// order, same reason codes and en/np texts, same order of reasons. Red is evaluated fully before
/// amber is considered at all; the A.6 cases #3–#11 are the shared tests. The server's answer is the
/// one the app displays if the two ever differ (A.4 PUT …/contacts/:contactNo).
/// </summary>
internal static class TriageEngine
{
    public static readonly TriageReason SevereHypertension = new(
        "SEVERE_HYPERTENSION", "Severe hypertension (≥160/110)", "अति उच्च रक्तचाप (≥१६०/११०)");

    public static readonly TriageReason PreEclampsiaSuspected = new(
        "PRE_ECLAMPSIA_SUSPECTED",
        "BP ≥ 140/90 with proteinuria or severe headache — possible pre-eclampsia",
        "रक्तचाप ≥ १४०/९० सँगै पिसाबमा प्रोटिन वा कडा टाउको दुखाइ — प्रि-एक्लाम्पसिया हुन सक्छ");

    public static readonly TriageReason SevereAnaemia = new(
        "SEVERE_ANAEMIA", "Severe anaemia (Hb < 7)", "गम्भीर रक्तअल्पता (हेमोग्लोबिन ७ भन्दा कम)");

    public static readonly TriageReason AbsentFetalMovement = new(
        "ABSENT_FETAL_MOVEMENT", "Absent fetal movement", "बच्चा नचल्ने");

    public static readonly TriageReason RaisedBp = new(
        "RAISED_BP", "Raised BP (≥140/90)", "उच्च रक्तचाप (≥१४०/९०)");

    public static readonly TriageReason Anaemia = new(
        "ANAEMIA", "Anaemia (Hb 7–9.9)", "रक्तअल्पता (हेमोग्लोबिन ७–९.९)");

    public static readonly TriageReason Proteinuria = new(
        "PROTEINURIA", "Proteinuria", "पिसाबमा प्रोटिन");

    public static readonly TriageReason ReducedFetalMovement = new(
        "REDUCED_FETAL_MOVEMENT_FINDING", "Reduced fetal movement", "बच्चाको चाल कम");

    public static readonly TriageReason HighRisk = new(
        "HIGH_RISK_PREGNANCY", "High-risk pregnancy (risk factors present)", "जोखिमयुक्त गर्भावस्था (जोखिम कारक छन्)");

    public static TriageResult Evaluate(TriageInput input, Func<string, DangerSignRule?> dangerSign)
    {
        var reasons = new List<TriageReason>();
        var red = false;
        var amber = false;

        var signs = input.DangerSigns.Select(dangerSign).Where(s => s is not null).Select(s => s!).ToList();

        // 1. Any danger sign the rule table marks red.
        var redSigns = signs.Where(s => s.IsRed).ToList();
        if (redSigns.Count > 0)
        {
            red = true;
            reasons.AddRange(redSigns.Select(FromDangerSign));
        }

        var findings = input.Findings;
        if (findings is not null)
        {
            var systolic = findings.BpSys ?? 0;
            var diastolic = findings.BpDia ?? 0;
            var proteinuria = findings.UrineProtein.IsProteinuria();
            // An unmeasured haemoglobin stays out of both anaemia rules rather than reading as zero.
            var haemoglobin = findings.HbGdl ?? 99;

            if (systolic >= AncRules.SevereBpSys || diastolic >= AncRules.SevereBpDia)
            {
                red = true;
                reasons.Add(SevereHypertension);
            }

            if ((systolic >= AncRules.RaisedBpSys || diastolic >= AncRules.RaisedBpDia) &&
                (proteinuria || input.DangerSigns.Contains(AncRules.SevereHeadacheCode, StringComparer.Ordinal)))
            {
                red = true;
                reasons.Add(PreEclampsiaSuspected);
            }

            if (haemoglobin < AncRules.SevereAnaemiaHb)
            {
                red = true;
                reasons.Add(SevereAnaemia);
            }

            // Before 20 weeks there is nothing to feel, so absence means nothing.
            if (findings.FetalMovement == FetalMovement.Absent && input.GestationalAgeDays >= AncRules.FetalMovementFromDays)
            {
                red = true;
                reasons.Add(AbsentFetalMovement);
            }

            if (!red)
            {
                if (systolic >= AncRules.RaisedBpSys || diastolic >= AncRules.RaisedBpDia)
                {
                    amber = true;
                    reasons.Add(RaisedBp);
                }
                if (haemoglobin >= AncRules.SevereAnaemiaHb && haemoglobin < AncRules.AnaemiaHb)
                {
                    amber = true;
                    reasons.Add(Anaemia);
                }
                if (proteinuria)
                {
                    amber = true;
                    reasons.Add(Proteinuria);
                }
                if (findings.FetalMovement == FetalMovement.Reduced)
                {
                    amber = true;
                    reasons.Add(ReducedFetalMovement);
                }
            }
        }

        if (!red)
        {
            var amberSigns = signs.Where(s => !s.IsRed).ToList();
            if (amberSigns.Count > 0)
            {
                amber = true;
                reasons.AddRange(amberSigns.Select(FromDangerSign));
            }

            if (input.RiskLevel == RiskLevel.High)
            {
                amber = true;
                reasons.Add(HighRisk);
            }
        }

        var level = red ? TriageLevel.Red : amber ? TriageLevel.Amber : TriageLevel.Green;
        return new TriageResult(level, reasons);
    }

    private static TriageReason FromDangerSign(DangerSignRule sign) => new(sign.Code, sign.En, sign.Np);
}
