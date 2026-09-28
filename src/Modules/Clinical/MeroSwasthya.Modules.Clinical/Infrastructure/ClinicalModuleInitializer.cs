using MeroSwasthya.Modules.Clinical.Application;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Shared.Modules;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Modules.Clinical.Infrastructure;

/// <summary>
/// Migrates schema <c>clinical</c> and seeds the demo history (ids from Part A / mock_api.dart where they
/// exist). Insert-if-missing; <c>updatedAt</c> = now (addendum §7). Document bytes are a placeholder JPEG
/// stored in MinIO when reachable, else on local disk.
/// </summary>
internal sealed class ClinicalModuleInitializer(
    ClinicalDbContext db,
    DocumentStorage storage,
    IClock clock,
    ILogger<ClinicalModuleInitializer> logger) : IModuleInitializer
{
    public const string RamId = "p_a1a1a1a1-0000-4000-8000-000000000002";
    public const string SitaId = "p_a1a1a1a1-0000-4000-8000-000000000001";
    public const string AaravId = "p_a1a1a1a1-0000-4000-8000-000000000006";
    public const string ProviderId = "u_22222222-2222-4222-8222-222222222222";
    public const string OwnerId = "u_11111111-1111-4111-8111-111111111111";
    public const string RamLatestVisitId = "v_c3c3c3c3-0000-4000-8000-000000000001";
    public const string RamLabDocumentId = "d_e5e5e5e5-0000-4000-8000-000000000001";
    public const string RamDischargeDocumentId = "d_e5e5e5e5-0000-4000-8000-000000000002";

    public string Module => "clinical";
    public int Order => 40;

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = clock.TodayUtc;

        Visit V(string id, string patientId, int daysAgo, string complaint, VitalsDto vitals, string[] diagnoses,
            PrescriptionDto[] rx, string? notes = null, string? advice = null, int? followUpDays = null)
        {
            // 04:05 UTC = 09:50 Nepal time — clinic hours.
            var at = today.AddDays(-daysAgo).ToDateTime(new TimeOnly(4, 5), DateTimeKind.Utc);
            return new Visit
            {
                Id = id, PatientId = patientId, ProviderUserId = ProviderId, ProviderName = "Ramesh Thapa (HA)",
                FacilityId = "f_0001", FacilityName = "Ghorahi Health Post", VisitAt = at,
                ChiefComplaintCode = complaint, Vitals = vitals, DiagnosisCodes = [.. diagnoses], Notes = notes, Advice = advice,
                FollowUpAt = followUpDays is { } d ? DateOnly.FromDateTime(at).AddDays(d) : null,
                Prescriptions = [.. rx], Version = 1, UpdatedAt = now, CreatedAt = now,
            };
        }

        PrescriptionDto Metformin(string id, int days) =>
            new(id, "METFORMIN_500", "Metformin 500 mg", "1 tab", PrescriptionFrequency.Bd, days, "खाना पछि");
        PrescriptionDto Amlodipine(string id, int days) =>
            new(id, "AMLODIPINE_5", "Amlodipine 5 mg", "1 tab", PrescriptionFrequency.Od, days, "बिहान");

        var visits = new[]
        {
            // Ram — diabetes + hypertension, four visits over twelve months; the latest is the Part A example.
            V("v_c3c3c3c3-0000-4000-8000-000000000004", RamId, 360, "CC_POLYURIA",
                new VitalsDto { BpSys = 146, BpDia = 92, Pulse = 80, WeightKg = 74.0 }, ["E11", "I10"],
                [Metformin("rx_0004_1", 90)], "Fasting sugar 212 mg/dl on glucometer.", "Diet, walk 30 min daily"),
            V("v_c3c3c3c3-0000-4000-8000-000000000003", RamId, 240, "CC_FOLLOW_UP",
                new VitalsDto { BpSys = 144, BpDia = 90, WeightKg = 73.0 }, ["E11", "I10"],
                [Metformin("rx_0003_1", 90), Amlodipine("rx_0003_2", 90)], "BP still above target; amlodipine started."),
            V("v_c3c3c3c3-0000-4000-8000-000000000002", RamId, 120, "CC_FOLLOW_UP",
                new VitalsDto { BpSys = 140, BpDia = 88, WeightKg = 72.5 }, ["E11", "I10"],
                [Metformin("rx_0002_1", 90), Amlodipine("rx_0002_2", 90)]),
            V(RamLatestVisitId, RamId, 25, "CC_POLYURIA",
                new VitalsDto { BpSys = 138, BpDia = 88, Pulse = 76, WeightKg = 71.5 }, ["E11", "I10"],
                [Metformin("rx_0001", 30), Amlodipine("rx_0002", 30)],
                "Fasting sugar 168 mg/dl on glucometer.", "Diet, walk 30 min daily", followUpDays: 30),

            // Sita — a fever a year ago.
            V("v_c3c3c3c3-0000-4000-8000-000000000005", SitaId, 365, "CC_FEVER",
                new VitalsDto { TempC = 38.6, Pulse = 96, WeightKg = 51.0 }, ["R50"],
                [new("rx_0005_1", "PARACETAMOL_500", "Paracetamol 500 mg", "1 tab", PrescriptionFrequency.Tds, 3, "ज्वरो आएमा")],
                advice: "Plenty of fluids; come back if fever lasts more than 3 days"),

            // Aarav — diarrhoea six months ago (ORS + zinc), cough two months ago.
            V("v_c3c3c3c3-0000-4000-8000-000000000006", AaravId, 182, "CC_DIARRHOEA",
                new VitalsDto { TempC = 37.4, WeightKg = 12.4 }, ["A09"],
                [
                    new("rx_0006_1", "ORS_SACHET", "ORS sachet", "1 sachet in 1 L water", PrescriptionFrequency.Sos, 5, "हरेक पातलो दिसा पछि"),
                    new("rx_0006_2", "ZINC_20", "Zinc 20 mg", "1 tab", PrescriptionFrequency.Od, 10, "१० दिनसम्म दिनको एक पटक"),
                ], advice: "Continue breastfeeding and food; watch for dehydration"),
            V("v_c3c3c3c3-0000-4000-8000-000000000007", AaravId, 60, "CC_COUGH",
                new VitalsDto { TempC = 37.8, Spo2 = 97, WeightKg = 12.9 }, ["J06"],
                [new("rx_0007_1", "PARACETAMOL_SYRUP", "Paracetamol syrup", "5 ml", PrescriptionFrequency.Tds, 3, "ज्वरो आएमा")]),
        };

        foreach (var visit in visits)
            if (!await db.Visits.AnyAsync(v => v.Id == visit.Id, ct))
                db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        // TODO(Reminders): the follow_up reminder for Ram's latest visit comes from the FollowUpScheduled subscriber.

        await SeedDocumentAsync(RamLabDocumentId, DocumentType.Lab, "Fasting blood sugar", today.AddDays(-40), now, ct);
        await SeedDocumentAsync(RamDischargeDocumentId, DocumentType.Discharge, "Bharatpur Hospital discharge sheet",
            today.AddDays(-12), now, ct);
    }

    private async Task SeedDocumentAsync(string id, DocumentType type, string title, DateOnly takenAt, DateTime now, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null)
        {
            doc = new Document
            {
                Id = id, PatientId = RamId, UploadedByUserId = ProviderId, Type = type, Title = title, TakenAt = takenAt,
                Status = DocumentStatus.Uploaded, ContentType = "image/jpeg", SizeBytes = Placeholder.Length,
                ObjectKey = DocumentStorage.ObjectKey(RamId, id, "image/jpeg"), AiSummaryStatus = AiSummaryStatus.None,
                Version = 1, UpdatedAt = now, CreatedAt = now,
            };
            db.Documents.Add(doc);
        }

        // (Re)store the bytes if they are missing — e.g. MinIO came up after a local-disk seed, or the disk was cleaned.
        if (!await storage.ExistsAsync(doc, ct))
        {
            await storage.StoreAsync(doc, Placeholder, ct);
            logger.LogInformation("Seed document {Id} stored in {Storage}", id, doc.Storage);
        }
        await db.SaveChangesAsync(ct);
    }

    private static readonly byte[] Placeholder = LoadPlaceholder();

    private static byte[] LoadPlaceholder()
    {
        using var stream = typeof(ClinicalModuleInitializer).Assembly
            .GetManifestResourceStream("MeroSwasthya.Clinical.placeholder-document.jpg")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
