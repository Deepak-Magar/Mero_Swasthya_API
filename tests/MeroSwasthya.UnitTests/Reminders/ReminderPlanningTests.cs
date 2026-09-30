using MeroSwasthya.Modules.Reminders.Application;
using MeroSwasthya.Modules.Reminders.Domain;

namespace MeroSwasthya.UnitTests.Reminders;

/// <summary>A.5 <c>reminders</c>: when each message goes out, what it says (A.2 Reminder example), and the BS date in it.</summary>
public sealed class ReminderPlanningTests
{
    private static readonly DateOnly Due = new(2026, 9, 18);

    [Theory]
    [InlineData("2026-09-18", "२०८३-०६-०२")] // the A.2 Reminder example
    [InlineData("2023-04-14", "२०८०-०१-०१")] // first day of the table
    [InlineData("2026-04-13", "२०८२-१२-३०")] // last day of 2082
    [InlineData("2026-04-14", "२०८३-०१-०१")] // Nepali new year 2083
    [InlineData("2026-09-30", "२०८३-०६-१४")]
    [InlineData("2044-04-13", "२१००-१२-३१")] // last day of the table
    public void Bs_date_matches_the_apps_calendar(string ad, string bs)
    {
        BsCalendar.Format(DateOnly.Parse(ad)).Should().Be(bs);
    }

    [Theory]
    [InlineData("2023-04-13")]
    [InlineData("2044-04-14")]
    public void A_date_outside_the_bs_table_falls_back_to_the_ad_date(string ad)
    {
        BsCalendar.FromAd(DateOnly.Parse(ad)).Should().BeNull();
        BsCalendar.Format(DateOnly.Parse(ad)).Should().Be(ad);
    }

    [Fact]
    public void Every_message_goes_out_at_nine_in_kathmandu()
    {
        // 09:00 at UTC+05:45 = 03:15 UTC (reminders_preview.dart).
        var sendAt = ReminderPlanner.SendAt(new DateOnly(2026, 9, 17));
        sendAt.Should().Be(new DateTime(2026, 9, 17, 3, 15, 0, DateTimeKind.Utc));
        sendAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void A_contact_gets_due_the_day_before_and_missed_after_three_and_seven_days()
    {
        var planned = ReminderPlanner.ForContact("Sita Chaudhary", "ac_4", 4, Due, "Ghorahi Health Post");

        planned.Select(p => (p.Kind, p.SourceKey, p.DueAt)).Should().Equal(
            (ReminderKind.AncDue, "anc_contact:ac_4:due", new DateTime(2026, 9, 17, 3, 15, 0, DateTimeKind.Utc)),
            (ReminderKind.AncMissed, "anc_contact:ac_4:missed:3", new DateTime(2026, 9, 21, 3, 15, 0, DateTimeKind.Utc)),
            (ReminderKind.AncMissed, "anc_contact:ac_4:missed:7", new DateTime(2026, 9, 25, 3, 15, 0, DateTimeKind.Utc)));
        planned.Should().OnlyContain(p => p.SourceKey.StartsWith(ReminderPlanner.ContactPrefix("ac_4")));
        planned[1].MessageEn.Should().Be(planned[2].MessageEn, "the second anc_missed repeats the first");
    }

    [Fact]
    public void A_follow_up_goes_out_the_day_before()
    {
        var planned = ReminderPlanner.ForFollowUp("Ram Bahadur Chaudhary", "v_1", new DateOnly(2026, 10, 18), "Ghorahi Health Post");

        planned.Kind.Should().Be(ReminderKind.FollowUp);
        planned.SourceKey.Should().Be("visit:v_1");
        planned.DueAt.Should().Be(new DateTime(2026, 10, 17, 3, 15, 0, DateTimeKind.Utc));
        planned.MessageEn.Should().Be("Ram Bahadur Chaudhary's follow-up visit is due on 2026-10-18 at Ghorahi Health Post.");
        planned.MessageNp.Should().Be("Ram Bahadur Chaudhary को फलो-अप जाँच २०८३-०७-०१ मा Ghorahi Health Post मा छ।");
    }

    [Fact]
    public void Anc_due_is_the_part_A_example_word_for_word()
    {
        var (np, en) = ReminderTexts.AncDue("सीता चौधरी", 4, Due, "घोराही स्वास्थ्य चौकी");
        np.Should().Be("सीता चौधरीको ४ औं गर्भ जाँच २०८३-०६-०२ मा घोराही स्वास्थ्य चौकीमा छ।");

        (_, en) = ReminderTexts.AncDue("Sita Chaudhary", 4, Due, "Ghorahi Health Post");
        en.Should().Be("Sita Chaudhary's ANC contact 4 is due on 2026-09-18 at Ghorahi Health Post.");
    }

    [Fact]
    public void A_name_in_latin_letters_is_set_off_from_the_nepali_postposition()
    {
        var (np, _) = ReminderTexts.AncDue("Sita Chaudhary", 8, Due, "Ghorahi Health Post");
        np.Should().Be("Sita Chaudhary को ८ औं गर्भ जाँच २०८३-०६-०२ मा Ghorahi Health Post मा छ।");
    }

    [Fact]
    public void Without_a_facility_the_clause_is_dropped()
    {
        ReminderTexts.AncDue("Sita Chaudhary", 4, Due, null).Should().Be((
            "Sita Chaudhary को ४ औं गर्भ जाँच २०८३-०६-०२ मा छ।",
            "Sita Chaudhary's ANC contact 4 is due on 2026-09-18."));
        ReminderTexts.FollowUp("Sita Chaudhary", Due, null).Should().Be((
            "Sita Chaudhary को फलो-अप जाँच २०८३-०६-०२ मा छ।",
            "Sita Chaudhary's follow-up visit is due on 2026-09-18."));
        ReminderTexts.AncMissed("Sita Chaudhary", 4, Due, null).Should().Be((
            "Sita Chaudhary को ४ औं गर्भ जाँच (२०८३-०६-०२) छुटेको छ। कृपया चाँडै स्वास्थ्य संस्थामा जानुहोस्।",
            "Sita Chaudhary missed ANC contact 4 (due 2026-09-18). Please visit a health facility as soon as possible."));
    }

    [Fact]
    public void Anc_missed_names_the_contact_its_date_and_the_facility()
    {
        ReminderTexts.AncMissed("Sita Chaudhary", 4, Due, "Ghorahi Health Post").Should().Be((
            "Sita Chaudhary को ४ औं गर्भ जाँच (२०८३-०६-०२) छुटेको छ। कृपया चाँडै Ghorahi Health Post मा जानुहोस्।",
            "Sita Chaudhary missed ANC contact 4 (due 2026-09-18). Please visit Ghorahi Health Post as soon as possible."));
    }
}
