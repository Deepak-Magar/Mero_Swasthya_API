using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Modules.Patients.Infrastructure;
using MeroSwasthya.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Patients.Application;

/// <summary>
/// Summary = Patients base (allergies, activeProblems from chronicConditions with codelist labels)
/// + every <see cref="IPatientSummaryContributor"/> in order (Clinical, Maternal — later sessions).
/// </summary>
internal sealed class PatientSummaryService(
    ICodeListLookup codes,
    IEnumerable<IPatientSummaryContributor> contributors) : IPatientSummaryService
{
    public async Task<PatientSummaryDto> BuildAsync(PatientDto patient, CancellationToken ct = default)
    {
        var builder = new PatientSummaryBuilder(patient);

        var labels = await codes.LabelsAsync(CodeListKind.Diagnosis, patient.ChronicConditions, ct);
        foreach (var code in patient.ChronicConditions)
        {
            var label = labels.GetValueOrDefault(code);
            // `since` needs the first visit that recorded the diagnosis — filled by the Clinical contributor.
            builder.ActiveProblems.Add(new ActiveProblemDto(code, label?.LabelEn ?? code, label?.LabelNp ?? code, Since: null));
        }

        foreach (var contributor in contributors.OrderBy(c => c.Order))
            await contributor.ContributeAsync(builder, ct);

        return builder.Build();
    }
}

/// <summary>Merges every module's timeline items into one newest-first page with a <c>before</c> cursor.</summary>
internal sealed class PatientTimelineService(IEnumerable<ITimelineContributor> contributors) : IPatientTimelineService
{
    public async Task<TimelinePageDto> GetPageAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default)
    {
        // Ask each contributor for one extra row so we know whether another page exists.
        var merged = new List<TimelineItemDto>();
        foreach (var contributor in contributors)
            merged.AddRange(await contributor.GetAsync(patientId, before, limit + 1, ct));

        var (page, next) = Cursors.PageDescending(merged, i => i.At, before, limit);
        return new TimelinePageDto(page, next);
    }
}

internal sealed class PatientDirectory(PatientsDbContext db) : IPatientDirectory
{
    public async Task<PatientDto?> FindAsync(string patientId, CancellationToken ct = default) =>
        (await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct))?.ToDto();
}
