using MeroSwasthya.Modules.Catalog.Contracts;
using MeroSwasthya.Modules.Catalog.Domain;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Modules.Clinical.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Clinical.Application;

internal static class VisitHistory
{
    /// <summary>Non-deleted visits minus the ones a later correction supersedes, newest first.</summary>
    public static List<Visit> Effective(IEnumerable<Visit> visits)
    {
        var all = visits.Where(v => !v.Deleted).ToList();
        var superseded = all.Where(v => v.SupersedesId is not null).Select(v => v.SupersedesId!).ToHashSet();
        return all.Where(v => !superseded.Contains(v.Id)).OrderByDescending(v => v.VisitAt).ThenByDescending(v => v.Id).ToList();
    }
}

/// <summary>
/// Fills the A.4 summary from visits: activeProblems (chronicConditions ∪ visit diagnoses, labelled,
/// <c>since</c> = first visit that recorded the code), currentMedicines (prescriptions still inside
/// their durationDays, newest per drugCode), lastVitals, lastVisitAt, visitCount.
/// </summary>
internal sealed class ClinicalSummaryContributor(ClinicalDbContext db, ICodeListLookup codes, IClock clock)
    : IPatientSummaryContributor
{
    public int Order => 10;

    public async Task ContributeAsync(PatientSummaryBuilder summary, CancellationToken ct = default)
    {
        var visits = VisitHistory.Effective(
            await db.Visits.AsNoTracking().Where(v => v.PatientId == summary.Patient.Id).ToListAsync(ct));
        if (visits.Count == 0) return;

        summary.VisitCount = visits.Count;
        summary.LastVisitAt = visits[0].VisitAt;

        // lastVitals carries bpSys / bpDia / weightKg (A.4), so it comes from the newest visit that measured one of them.
        var withVitals = visits.FirstOrDefault(v => v.Vitals.BpSys is not null || v.Vitals.BpDia is not null || v.Vitals.WeightKg is not null);
        if (withVitals is not null)
            summary.LastVitals = new LastVitalsDto(withVitals.Vitals.BpSys, withVitals.Vitals.BpDia, withVitals.Vitals.WeightKg, withVitals.VisitAt);

        var today = clock.TodayUtc;
        var seenDrugs = new HashSet<string>();
        foreach (var visit in visits)
        foreach (var rx in visit.Prescriptions)
        {
            var lastDay = DateOnly.FromDateTime(visit.VisitAt).AddDays(rx.DurationDays);
            if (lastDay >= today && seenDrugs.Add(rx.DrugCode))
                summary.CurrentMedicines.Add(rx);
        }

        // since = the first (oldest) visit that recorded each diagnosis.
        var since = new Dictionary<string, DateOnly>();
        foreach (var visit in visits.AsEnumerable().Reverse())
        foreach (var code in visit.DiagnosisCodes)
            since.TryAdd(code, DateOnly.FromDateTime(visit.VisitAt));

        for (var i = 0; i < summary.ActiveProblems.Count; i++)
        {
            var problem = summary.ActiveProblems[i];
            if (since.TryGetValue(problem.Code, out var first))
                summary.ActiveProblems[i] = problem with { Since = first };
        }

        var known = summary.ActiveProblems.Select(p => p.Code).ToHashSet();
        var added = since.Where(kv => !known.Contains(kv.Key)).OrderBy(kv => kv.Value).ToList();
        var labels = await codes.LabelsAsync(CodeListKind.Diagnosis, added.Select(a => a.Key), ct);
        foreach (var (code, first) in added)
        {
            var label = labels.GetValueOrDefault(code);
            summary.ActiveProblems.Add(new ActiveProblemDto(code, label?.LabelEn ?? code, label?.LabelNp ?? code, first));
        }
    }
}

/// <summary>Timeline kinds <c>visit</c> and <c>document</c>.</summary>
internal sealed class ClinicalTimelineContributor(ClinicalDbContext db, ICodeListLookup codes, DocumentMapper documents)
    : ITimelineContributor
{
    private static readonly Dictionary<DocumentType, string> DocumentTypeLabels = new()
    {
        [DocumentType.Prescription] = "Prescription",
        [DocumentType.Lab] = "Lab report",
        [DocumentType.Discharge] = "Discharge sheet",
        [DocumentType.Referral] = "Referral letter",
        [DocumentType.Other] = "Document",
    };

    public async Task<IReadOnlyList<TimelineItemDto>> GetAsync(string patientId, DateTime? before, int limit, CancellationToken ct = default)
    {
        var visitQuery = db.Visits.AsNoTracking().Where(v => v.PatientId == patientId && !v.Deleted);
        if (before is not null) visitQuery = visitQuery.Where(v => v.VisitAt < before);
        var visits = await visitQuery.OrderByDescending(v => v.VisitAt).Take(limit).ToListAsync(ct);

        var docQuery = db.Documents.AsNoTracking().Where(d => d.PatientId == patientId && !d.Deleted);
        if (before is not null)
        {
            // A document sits on the timeline at midnight UTC of the date on the paper (exact cut below).
            var at = before.Value;
            var cutoff = DateOnly.FromDateTime(at.TimeOfDay == TimeSpan.Zero ? at.AddDays(-1) : at);
            docQuery = docQuery.Where(d => d.TakenAt <= cutoff);
        }
        var docs = await docQuery.OrderByDescending(d => d.TakenAt).Take(limit).ToListAsync(ct);

        var complaintLabels = await codes.LabelsAsync(CodeListKind.Complaint, visits.Select(v => v.ChiefComplaintCode), ct);
        var diagnosisLabels = await codes.LabelsAsync(CodeListKind.Diagnosis, visits.SelectMany(v => v.DiagnosisCodes), ct);

        var items = new List<TimelineItemDto>(visits.Count + docs.Count);
        foreach (var v in visits)
        {
            var complaint = complaintLabels.GetValueOrDefault(v.ChiefComplaintCode)?.LabelEn ?? v.ChiefComplaintCode;
            var firstDiagnosis = v.DiagnosisCodes.FirstOrDefault();
            var headline = firstDiagnosis is null
                ? complaint
                : diagnosisLabels.GetValueOrDefault(firstDiagnosis)?.LabelEn ?? firstDiagnosis;

            // "Visit — Ghorahi Health Post — Type 2 diabetes"
            var title = string.Join(" — ", new[] { "Visit", v.FacilityName, headline }.Where(s => !string.IsNullOrWhiteSpace(s)));
            // "Frequent urination · BP 138/88"
            var subtitle = v.Vitals.BpSys is { } sys && v.Vitals.BpDia is { } dia ? $"{complaint} · BP {sys}/{dia}" : complaint;

            items.Add(new TimelineItemDto(TimelineKind.Visit, v.VisitAt, title, subtitle, null, v.Id, v.ToDto()));
        }

        foreach (var d in docs)
        {
            var at = d.TakenAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            if (before is not null && at >= before) continue;
            var subtitle = DocumentTypeLabels[d.Type] + (d.Status == DocumentStatus.PendingUpload ? " · uploading" : "");
            items.Add(new TimelineItemDto(TimelineKind.Document, at, d.Title, subtitle, null, d.Id, documents.ToDto(d)));
        }

        return items.OrderByDescending(i => i.At).Take(limit).ToList();
    }
}
