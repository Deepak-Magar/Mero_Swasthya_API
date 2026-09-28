using System.Text.Json;
using FluentValidation;
using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Patients.Application;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Domain;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Time;
using MeroSwasthya.Shared.Validation;

namespace MeroSwasthya.UnitTests.Patients;

public sealed class PatientReadModelTests
{
    private static readonly DateTime T0 = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTimeline(TimelineKind kind, params int[] minutes) : ITimelineContributor
    {
        public Task<IReadOnlyList<TimelineItemDto>> GetAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TimelineItemDto>>(minutes
                .Select(m => new TimelineItemDto(kind, T0.AddMinutes(m), $"{kind} {m}", null, null, $"{kind}-{m}", new { }))
                .Where(i => before is null || i.At < before)
                .OrderByDescending(i => i.At)
                .Take(limit)
                .ToList());
    }

    [Fact]
    public async Task Timeline_merges_contributors_newest_first_and_pages_with_before()
    {
        var service = new PatientTimelineService([
            new FakeTimeline(TimelineKind.Visit, 1, 4, 7),
            new FakeTimeline(TimelineKind.AncContact, 2, 5, 8),
        ]);

        var page1 = await service.GetPageAsync("p", null, 4);
        page1.Items.Select(i => i.RefId).Should().Equal("AncContact-8", "Visit-7", "AncContact-5", "Visit-4");
        page1.NextBefore.Should().Be(T0.AddMinutes(4));

        var page2 = await service.GetPageAsync("p", page1.NextBefore, 4);
        page2.Items.Select(i => i.RefId).Should().Equal("AncContact-2", "Visit-1");
        page2.NextBefore.Should().BeNull();
    }

    [Fact]
    public async Task Timeline_without_contributors_is_an_empty_last_page()
    {
        var page = await new PatientTimelineService([]).GetPageAsync("p", null, 50);
        page.Items.Should().BeEmpty();
        page.NextBefore.Should().BeNull();
    }

    [Fact]
    public void Timeline_item_serialises_to_the_part_A_shape()
    {
        var item = new TimelineItemDto(TimelineKind.PregnancyRegistered, T0, "Pregnancy registered", null, TimelineBadge.Red, "pg_1", new { id = "pg_1" });
        JsonSerializer.Serialize(item, JsonDefaults.Options).Should().Be(
            "{\"kind\":\"pregnancy_registered\",\"at\":\"2026-09-18T00:00:00.000Z\",\"title\":\"Pregnancy registered\"," +
            "\"subtitle\":null,\"badge\":\"red\",\"refId\":\"pg_1\",\"payload\":{\"id\":\"pg_1\"}}");
    }

    private sealed class FakeCodes : ICodeListLookup
    {
        public Task<IReadOnlyDictionary<string, CodeLabel>> LabelsAsync(CodeListKind kind, IEnumerable<string> codes, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, CodeLabel>>(new Dictionary<string, CodeLabel>
            {
                ["E11"] = new("E11", "Type 2 diabetes", "मधुमेह"),
            });

        public Task<string> VersionAsync(CancellationToken ct = default) => Task.FromResult("v");
    }

    private sealed class Contributor(int order, Action<PatientSummaryBuilder> apply) : IPatientSummaryContributor
    {
        public int Order => order;

        public Task ContributeAsync(PatientSummaryBuilder summary, CancellationToken ct = default)
        {
            apply(summary);
            return Task.CompletedTask;
        }
    }

    private static PatientDto Patient(params string[] chronic) => new(
        "p", "u", "Ram", Sex.Male, new DateOnly(1968, 1, 15), null, null, null, ["penicillin"], chronic, null, 1, T0, false);

    [Fact]
    public async Task Summary_labels_known_codes_falls_back_to_the_code_and_runs_contributors_in_order()
    {
        var calls = new List<int>();
        var service = new PatientSummaryService(new FakeCodes(), [
            new Contributor(20, s => { calls.Add(20); s.ActivePregnancy = new { id = "pg" }; }),
            new Contributor(10, s => { calls.Add(10); s.VisitCount = 4; }),
        ]);

        var summary = await service.BuildAsync(Patient("E11", "X99"));

        summary.ActiveProblems.Should().Equal(
            new ActiveProblemDto("E11", "Type 2 diabetes", "मधुमेह", null),
            new ActiveProblemDto("X99", "X99", "X99", null));
        summary.Allergies.Should().Equal("penicillin");
        summary.VisitCount.Should().Be(4);
        summary.ActivePregnancy.Should().NotBeNull();
        calls.Should().Equal(10, 20);
    }

    [Fact]
    public void Empty_summary_serialises_with_every_key_present()
    {
        JsonSerializer.Serialize(PatientSummaryDto.Empty, JsonDefaults.Options).Should().Be(
            "{\"activeProblems\":[],\"currentMedicines\":[],\"allergies\":[],\"lastVitals\":null," +
            "\"activePregnancy\":null,\"lastVisitAt\":null,\"visitCount\":0}");
    }
}

public sealed class PatientValidatorTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);
    }

    static PatientValidatorTests() => ValidationExtensions.ConfigureGlobal();

    [Fact]
    public void Patch_errors_are_reported_under_json_field_names()
    {
        var request = JsonSerializer.Deserialize<PatchPatientRequest>(
            """{ "name": "", "dob": "2027-01-01", "bloodGroup": "Z" }""", JsonDefaults.Options)!;
        var result = new PatchPatientRequestValidator(new FixedClock()).Validate(request);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["version", "name", "dob", "bloodGroup"]);
    }

    [Fact]
    public void Patch_with_only_version_is_valid()
    {
        var request = JsonSerializer.Deserialize<PatchPatientRequest>("""{ "version": 3 }""", JsonDefaults.Options)!;
        new PatchPatientRequestValidator(new FixedClock()).Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Create_accepts_the_part_A_example()
    {
        var request = JsonSerializer.Deserialize<CreatePatientRequest>("""
            {
              "id": "p_a1a1a1a1-0000-4000-8000-000000000001", "name": "Sita Chaudhary", "sex": "female",
              "dob": "2002-04-11", "bloodGroup": "B+", "ward": 5, "municipality": "Ghorahi",
              "allergies": [], "chronicConditions": [], "emergencyContactPhone": "+9779801000009"
            }
            """, JsonDefaults.Options)!;
        new CreatePatientRequestValidator(new FixedClock()).Validate(request).IsValid.Should().BeTrue();
    }
}
