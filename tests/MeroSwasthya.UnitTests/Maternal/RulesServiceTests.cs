using System.Text.Json;
using MeroSwasthya.Modules.Catalog.Application;
using MeroSwasthya.Modules.Maternal.Application;
using MeroSwasthya.Modules.Maternal.Contracts;
using MeroSwasthya.Modules.Maternal.Domain;
using MeroSwasthya.Shared.Json;

namespace MeroSwasthya.UnitTests.Maternal;

/// <summary>A.5 RULES as in-code data, and the A.6 schedule cases (#1, #2).</summary>
public sealed class RulesServiceTests
{
    private readonly RulesService _rules = new();

    [Fact]
    public void Version_is_the_one_GET_rules_serves()
    {
        _rules.Version.Should().Be(new RulesProvider().Version).And.Be("2026-09-18.1");
    }

    [Fact]
    public void In_code_rules_equal_the_document_served_by_GET_rules()
    {
        // GET /rules is the app's rules.json verbatim (Catalog); the triage engine runs on this file's copy.
        var doc = new RulesProvider().Document;

        var schedule = doc.GetProperty("ancSchedule").EnumerateArray().Select(e => (
            ContactNo: e.GetProperty("contactNo").GetInt32(),
            WeekTarget: e.GetProperty("weekTarget").GetInt32(),
            Checklist: e.GetProperty("checklist").EnumerateArray().Select(c => c.GetString()!).ToList())).ToList();
        _rules.Schedule.Select(s => (s.ContactNo, s.WeekTarget, Checklist: s.Checklist.ToList()))
            .Should().BeEquivalentTo(schedule, o => o.WithStrictOrdering());

        var signs = doc.GetProperty("dangerSigns").EnumerateArray().Select(e => (
            Code: e.GetProperty("code").GetString(),
            Level: e.GetProperty("level").GetString(),
            En: e.GetProperty("en").GetString(),
            Np: e.GetProperty("np").GetString())).ToList();
        _rules.DangerSigns.Select(d => (Code: (string?)d.Code, Level: (string?)d.Level.ToWire(), En: (string?)d.En, Np: (string?)d.Np))
            .Should().BeEquivalentTo(signs, o => o.WithStrictOrdering());

        var factors = doc.GetProperty("riskFactors").EnumerateArray().Select(e => (
            Code: e.GetProperty("code").GetString(),
            En: e.GetProperty("en").GetString(),
            Np: e.GetProperty("np").GetString())).ToList();
        _rules.RiskFactors.Select(r => (Code: (string?)r.Code, En: (string?)r.En, Np: (string?)r.Np))
            .Should().BeEquivalentTo(factors, o => o.WithStrictOrdering());

        // The thresholds in the triage prose.
        var triage = doc.GetProperty("triage");
        string.Join("\n", triage.GetProperty("red").EnumerateArray().Select(r => r.GetString()))
            .Should().Contain($"bpSys >= {AncRules.SevereBpSys}").And.Contain($"bpDia >= {AncRules.SevereBpDia}")
            .And.Contain($"hbGdl < {AncRules.SevereAnaemiaHb}").And.Contain($"gestationalAgeDays >= {AncRules.FetalMovementFromDays}");
        string.Join("\n", triage.GetProperty("amber").EnumerateArray().Select(r => r.GetString()))
            .Should().Contain($"bpSys >= {AncRules.RaisedBpSys}").And.Contain($"bpDia >= {AncRules.RaisedBpDia}")
            .And.Contain($"hbGdl < {AncRules.AnaemiaHb}");
    }

    [Fact]
    public void Schedule_has_the_eight_contacts_at_12_20_26_30_34_36_38_40_weeks()
    {
        _rules.Schedule.Select(s => s.ContactNo).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        _rules.Schedule.Select(s => s.WeekTarget).Should().Equal(12, 20, 26, 30, 34, 36, 38, 40);
    }

    [Fact]
    public void A6_case_1_edd_and_contact_due_dates_from_lmp()
    {
        var lmp = new DateOnly(2026, 2, 20);
        _rules.EddFromLmp(lmp).Should().Be(new DateOnly(2026, 11, 27));

        var schedule = _rules.ScheduleFor(lmp);
        schedule.Should().HaveCount(8);
        schedule.Single(c => c.ContactNo == 1).Should().Be(new ScheduledContact(1, 12, new DateOnly(2026, 5, 15)));
        schedule.Single(c => c.ContactNo == 5).Should().Be(new ScheduledContact(5, 34, new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public void A6_case_2_lmp_is_derived_from_edd_and_gestational_age_counts_from_it()
    {
        var edd = new DateOnly(2026, 11, 27);
        _rules.LmpFromEdd(edd).Should().Be(new DateOnly(2026, 2, 20));
        _rules.GestationalAgeDays(edd, new DateOnly(2026, 9, 18)).Should().Be(210);
        _rules.ScheduleFor(_rules.LmpFromEdd(edd)).Single(c => c.ContactNo == 1).DueAt.Should().Be(new DateOnly(2026, 5, 15));
    }

    [Fact]
    public void Gestational_age_is_negative_before_the_lmp_and_keeps_counting_past_the_edd()
    {
        var edd = new DateOnly(2026, 11, 27);
        _rules.GestationalAgeDays(edd, new DateOnly(2026, 2, 10)).Should().Be(-10);
        _rules.GestationalAgeDays(edd, new DateOnly(2026, 12, 4)).Should().Be(287);
    }

    [Fact]
    public void Risk_level_is_high_when_any_risk_factor_is_present()
    {
        _rules.RiskLevelFor([]).Should().Be(RiskLevel.Normal);
        _rules.RiskLevelFor(["PREV_CS"]).Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Lookups_by_code()
    {
        _rules.DangerSign("SEVERE_HEADACHE_BLURRED_VISION")!.IsRed.Should().BeTrue();
        _rules.DangerSign("SWELLING_FACE_HANDS")!.Level.Should().Be(DangerSignLevel.Amber);
        _rules.DangerSign("NOT_A_SIGN").Should().BeNull();
        _rules.RiskFactor("GRAND_MULTIPARA")!.En.Should().Be("Para ≥ 5");
        _rules.RiskFactor("NOPE").Should().BeNull();
    }

    [Theory]
    [InlineData("neg", false)]
    [InlineData("trace", false)]
    [InlineData("+", true)]
    [InlineData("++", true)]
    [InlineData("+++", true)]
    public void Proteinuria_means_plus_or_more(string wire, bool proteinuria)
    {
        UrineProtein? value = WireEnum.Parse<UrineProtein>(wire);
        value.IsProteinuria().Should().Be(proteinuria);
        JsonSerializer.Serialize(value, JsonDefaults.Options).Should().Be($"\"{wire}\"");
    }
}
