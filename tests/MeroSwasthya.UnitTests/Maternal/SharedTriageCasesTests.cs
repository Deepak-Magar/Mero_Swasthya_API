using System.Text.Json;
using MeroSwasthya.Modules.Maternal.Application;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.UnitTests.Maternal;

/// <summary>
/// A.6 "Shared test cases (both sides must pass)", numbered as in the table. #1–#2 (EDD / schedule)
/// and #3–#11 (triage) are pure rules and live here. #12 (male → 422 RULE_VIOLATION) is a server
/// rule covered by the Maternal contract tests; #13–#14 are Sync (Session 4); #15–#16 are Grants and
/// pass in GrantsTests (Session 2).
/// </summary>
public sealed class SharedTriageCasesTests
{
    private readonly RulesService _rules = new();

    private TriageResult Triage(
        FindingsDto? findings = null,
        string[]? dangerSigns = null,
        string[]? riskFactors = null,
        int gestationalAgeDays = 210) =>
        _rules.Triage(new TriageInput(findings, dangerSigns ?? [], _rules.RiskLevelFor(riskFactors ?? []), gestationalAgeDays));

    [Fact]
    public void Case_01_lmp_gives_edd_and_contact_1_and_5_due_dates()
    {
        var lmp = new DateOnly(2026, 2, 20);
        _rules.EddFromLmp(lmp).Should().Be(new DateOnly(2026, 11, 27));
        var schedule = _rules.ScheduleFor(lmp).ToDictionary(c => c.ContactNo);
        schedule[1].WeekTarget.Should().Be(12);
        schedule[1].DueAt.Should().Be(new DateOnly(2026, 5, 15));
        schedule[5].WeekTarget.Should().Be(34);
        schedule[5].DueAt.Should().Be(new DateOnly(2026, 10, 16));
    }

    [Fact]
    public void Case_02_edd_only_derives_lmp_for_scheduling_and_gestational_age()
    {
        var edd = new DateOnly(2026, 11, 27);
        _rules.LmpFromEdd(edd).Should().Be(new DateOnly(2026, 2, 20));
        _rules.GestationalAgeDays(edd, new DateOnly(2026, 9, 18)).Should().Be(210);
    }

    [Fact]
    public void Case_03_bp_150_95_with_severe_headache_is_red_with_the_pre_eclampsia_reason()
    {
        var result = Triage(new FindingsDto { BpSys = 150, BpDia = 95 }, ["SEVERE_HEADACHE_BLURRED_VISION"]);
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Should().Contain(TriageEngine.PreEclampsiaSuspected);
        // The reasons a health worker reads, in the order the rule table lists them.
        result.ReasonsEn.Should().Equal(
            "Severe headache with blurred vision",
            "BP ≥ 140/90 with proteinuria or severe headache — possible pre-eclampsia");
    }

    [Fact]
    public void Case_04_bp_142_88_alone_is_amber()
    {
        var result = Triage(new FindingsDto { BpSys = 142, BpDia = 88 });
        result.Level.Should().Be(TriageLevel.Amber);
        result.Reasons.Should().Equal(TriageEngine.RaisedBp);
    }

    [Fact]
    public void Case_05_hb_6_8_is_red()
    {
        var result = Triage(new FindingsDto { HbGdl = 6.8 });
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Should().Equal(TriageEngine.SevereAnaemia);
    }

    [Fact]
    public void Case_06_hb_9_2_is_amber()
    {
        var result = Triage(new FindingsDto { HbGdl = 9.2 });
        result.Level.Should().Be(TriageLevel.Amber);
        result.Reasons.Should().Equal(TriageEngine.Anaemia);
    }

    [Fact]
    public void Case_07_absent_fetal_movement_at_150_days_is_red()
    {
        var result = Triage(new FindingsDto { FetalMovement = FetalMovement.Absent }, gestationalAgeDays: 150);
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Should().Equal(TriageEngine.AbsentFetalMovement);
    }

    [Fact]
    public void Case_08_absent_fetal_movement_at_120_days_is_not_red_by_that_rule()
    {
        var result = Triage(new FindingsDto { FetalMovement = FetalMovement.Absent }, gestationalAgeDays: 120);
        result.Level.Should().Be(TriageLevel.Green, "before 20 weeks the rule does not apply and nothing else matched");
        result.Reasons.Should().BeEmpty();

        // …but the rest is still evaluated: the same finding with raised BP is amber.
        Triage(new FindingsDto { FetalMovement = FetalMovement.Absent, BpSys = 140 }, gestationalAgeDays: 120)
            .Level.Should().Be(TriageLevel.Amber);
    }

    [Fact]
    public void Case_09_amber_danger_sign_with_no_findings_is_amber()
    {
        var result = Triage(dangerSigns: ["SWELLING_FACE_HANDS"]);
        result.Level.Should().Be(TriageLevel.Amber);
        result.Reasons.Should().Equal(new TriageReason("SWELLING_FACE_HANDS", "Swelling of face and hands", "अनुहार र हात सुन्निनु"));
    }

    [Fact]
    public void Case_10_risk_factor_alone_is_amber()
    {
        var result = Triage(riskFactors: ["PREV_CS"]);
        result.Level.Should().Be(TriageLevel.Amber);
        result.Reasons.Should().Equal(TriageEngine.HighRisk);
    }

    [Fact]
    public void Case_11_nothing_at_all_is_green()
    {
        var result = Triage();
        result.Level.Should().Be(TriageLevel.Green);
        result.Reasons.Should().BeEmpty();
    }

    // ---- Beyond the table: the remaining A.5 rows and the evaluation order.

    [Theory]
    [InlineData(160, 80)]
    [InlineData(120, 110)]
    public void Bp_at_or_above_160_or_110_is_red_by_itself(int sys, int dia)
    {
        var result = Triage(new FindingsDto { BpSys = sys, BpDia = dia });
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Should().Equal(TriageEngine.SevereHypertension);
    }

    [Fact]
    public void Bp_140_90_with_proteinuria_is_red_and_carries_no_amber_reasons()
    {
        var result = Triage(new FindingsDto { BpSys = 140, BpDia = 90, UrineProtein = UrineProtein.Two });
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Should().Equal(TriageEngine.PreEclampsiaSuspected);
    }

    [Fact]
    public void Proteinuria_alone_is_amber_but_trace_is_not()
    {
        Triage(new FindingsDto { UrineProtein = UrineProtein.One }).Should().BeEquivalentTo(
            new TriageResult(TriageLevel.Amber, [TriageEngine.Proteinuria]));
        Triage(new FindingsDto { UrineProtein = UrineProtein.Trace }).Level.Should().Be(TriageLevel.Green);
    }

    [Fact]
    public void Reduced_fetal_movement_is_amber_at_any_gestational_age()
    {
        Triage(new FindingsDto { FetalMovement = FetalMovement.Reduced }, gestationalAgeDays: 100).Should().BeEquivalentTo(
            new TriageResult(TriageLevel.Amber, [TriageEngine.ReducedFetalMovement]));
    }

    [Fact]
    public void Red_danger_signs_and_severe_bp_list_every_red_reason_and_skip_amber_ones()
    {
        var result = Triage(
            new FindingsDto { BpSys = 165, BpDia = 100, HbGdl = 9.0 },
            ["VAGINAL_BLEEDING", "PERSISTENT_VOMITING"],
            ["AGE_GT_35"]);
        result.Level.Should().Be(TriageLevel.Red);
        result.Reasons.Select(r => r.Code).Should().Equal("VAGINAL_BLEEDING", "SEVERE_HYPERTENSION");
    }

    [Fact]
    public void Amber_reasons_come_in_rule_order_findings_then_signs_then_risk()
    {
        var result = Triage(
            // (BP stays below 140/90: raised BP together with proteinuria would be the red pre-eclampsia rule.)
            new FindingsDto { BpSys = 130, BpDia = 85, HbGdl = 8, UrineProtein = UrineProtein.One, FetalMovement = FetalMovement.Reduced },
            ["PERSISTENT_VOMITING", "SWELLING_FACE_HANDS"],
            ["HEIGHT_LT_145"]);
        result.Level.Should().Be(TriageLevel.Amber);
        result.Reasons.Select(r => r.Code).Should().Equal(
            "ANAEMIA", "PROTEINURIA", "REDUCED_FETAL_MOVEMENT_FINDING",
            "PERSISTENT_VOMITING", "SWELLING_FACE_HANDS", "HIGH_RISK_PREGNANCY");
    }

    [Fact]
    public void Unknown_danger_sign_codes_are_ignored_and_the_same_input_always_gives_the_same_answer()
    {
        var input = new TriageInput(new FindingsDto { BpSys = 150, BpDia = 95 }, ["NOT_A_SIGN", "SEVERE_HEADACHE_BLURRED_VISION"], RiskLevel.Normal, 210);
        var first = _rules.Triage(input);
        var second = _rules.Triage(input);
        first.Should().BeEquivalentTo(second, o => o.WithStrictOrdering());
        first.Reasons.Select(r => r.Code).Should().Equal("SEVERE_HEADACHE_BLURRED_VISION", "PRE_ECLAMPSIA_SUSPECTED");
    }

    [Fact]
    public void Findings_serialise_like_the_part_A_example_with_omitted_keys_omitted()
    {
        var json = """
            {"weightKg":58,"bpSys":150,"bpDia":95,"fundalHeightCm":29,"fhrBpm":142,"hbGdl":9.2,"urineProtein":"trace","tdDoseGiven":false,"ifaGiven":true,"dewormingGiven":true,"calciumGiven":true,"fetalMovement":"normal"}
            """.Trim();
        var findings = JsonSerializer.Deserialize<FindingsDto>(json, JsonDefaults.Options)!;
        findings.UrineProtein.Should().Be(UrineProtein.Trace);
        findings.FetalMovement.Should().Be(FetalMovement.Normal);
        JsonSerializer.Serialize(findings, JsonDefaults.Options).Should().Be(json);

        // Addendum §5: notesText rides inside findings; unmeasured keys are omitted.
        JsonSerializer.Serialize(new FindingsDto { BpSys = 124, NotesText = "आराम गर्न सल्लाह दिइयो।" }, JsonDefaults.Options)
            .Should().Be("""{"bpSys":124,"notesText":"आराम गर्न सल्लाह दिइयो।"}""");
    }
}
