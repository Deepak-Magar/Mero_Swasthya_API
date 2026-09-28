using MeroSwasthya.Modules.Audit.Contracts;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Modules.Clinical.Infrastructure;
using MeroSwasthya.Modules.Patients.Contracts;
using MeroSwasthya.Shared;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Json;
using MeroSwasthya.Shared.Security;
using MeroSwasthya.Shared.Time;
using Microsoft.EntityFrameworkCore;

namespace MeroSwasthya.Modules.Clinical.Application;

/// <summary>Turns a Document row into its A.2 DTO with a fresh download URL.</summary>
internal sealed class DocumentMapper(DocumentStorage storage, IClock clock)
{
    public DocumentDto ToDto(Document d) => new(
        d.Id, d.PatientId, d.UploadedByUserId, d.Type, d.Title, d.TakenAt, d.Status,
        storage.DownloadUrl(d, clock.UtcNow), d.AiSummary, d.AiSummaryStatus, d.Version, d.UpdatedAt, d.Deleted);
}

/// <summary>A.4 "Documents (paper capture)": presign → PUT bytes → complete; read; summarize.</summary>
internal sealed class DocumentService(
    ClinicalDbContext db,
    IPatientAccess access,
    ICurrentUser currentUser,
    DocumentStorage storage,
    DocumentMapper mapper,
    DocumentUrlSigner signer,
    IAuditWriter audit,
    FeatureFlags features,
    IClock clock)
{
    public async Task<PresignResponse> PresignAsync(PresignDocumentRequest request, CancellationToken ct)
    {
        await access.RequireAppendAsync(request.PatientId!, ct);
        var user = await currentUser.GetAsync(ct);
        var now = clock.UtcNow;

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (doc is not null && doc.PatientId != request.PatientId)
            throw AppException.Validation("id", "This id is already used by a document of another patient");

        if (doc is null)
        {
            doc = new Document
            {
                Id = request.Id!,
                PatientId = request.PatientId!,
                UploadedByUserId = user.Id,
                Type = WireEnum.Parse<DocumentType>(request.Type!),
                Title = request.Title!.Trim(),
                TakenAt = request.TakenAt!.Value,
                Status = DocumentStatus.PendingUpload,
                ContentType = request.ContentType!,
                SizeBytes = request.SizeBytes!.Value,
                ObjectKey = DocumentStorage.ObjectKey(request.PatientId!, request.Id!, request.ContentType!),
                AiSummaryStatus = AiSummaryStatus.None,
                Version = 1,
                UpdatedAt = now,
                CreatedAt = now,
            };
            db.Documents.Add(doc);
        }

        // Idempotent: presigning the same id again (an upload retry) just hands out a fresh URL.
        var uploadUrl = await storage.UploadUrlAsync(doc, now, ct);
        await db.SaveChangesAsync(ct);

        return new PresignResponse(
            mapper.ToDto(doc), uploadUrl, "PUT",
            new Dictionary<string, string> { ["Content-Type"] = doc.ContentType },
            (int)StorageOptions.UploadTtl.TotalSeconds);
    }

    /// <summary>
    /// Bytes sent to this API — the proxy PUT (<c>uploadUrl</c>) or the multipart dev fallback. Allowed with a
    /// valid upload signature, or with a bearer token that has append access to the patient.
    /// </summary>
    public async Task<DocumentDto> ReceiveAsync(string id, byte[] bytes, string? contentType, bool signatureValid, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Document");
        if (!signatureValid) await access.RequireAppendAsync(doc.PatientId, ct);

        if (bytes.Length == 0) throw AppException.Validation("file", "The upload is empty");
        if (bytes.Length > StorageOptions.MaxBytes) throw AppException.Validation("file", "Documents must be at most 2 MB");
        var type = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (type is not null && !StorageOptions.ContentTypes.Contains(type))
            throw AppException.Validation("contentType", "Must be image/jpeg or image/png");

        await storage.StoreAsync(doc, bytes, ct);
        await db.SaveChangesAsync(ct);
        return mapper.ToDto(doc);
    }

    public async Task<DocumentDto> CompleteAsync(string id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, ct) ?? throw AppException.NotFound("Document");
        await access.RequireAppendAsync(doc.PatientId, ct);
        if (doc.Status == DocumentStatus.Uploaded) return mapper.ToDto(doc); // idempotent

        if (!await storage.ExistsAsync(doc, ct))
            throw AppException.RuleViolation("No uploaded file found for this document; PUT the bytes to uploadUrl first");

        doc.Status = DocumentStatus.Uploaded;
        doc.Version++;
        doc.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(doc.PatientId, AuditAction.DocumentAdded, ct);
        return mapper.ToDto(doc);
    }

    public async Task<DocumentDto> GetAsync(string id, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, ct)
                  ?? throw AppException.NotFound("Document");
        await access.RequireReadAsync(doc.PatientId, ct);
        return mapper.ToDto(doc);
    }

    public async Task<DocumentDto> SummarizeAsync(string id, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, ct) ?? throw AppException.NotFound("Document");
        await access.RequireReadAsync(doc.PatientId, ct);

        // A.4: 501 when AI is off; GET /config reports aiSummaryEnabled=false so the app hides the button.
        if (!features.AiSummaryEnabled)
            throw new AppException(ErrorCode.NotImplemented, "AI summaries are not enabled on this server");
        if (doc.Status != DocumentStatus.Uploaded)
            throw AppException.RuleViolation("The document has not been uploaded yet");

        if (doc.AiSummaryStatus is not AiSummaryStatus.Queued)
        {
            // TODO(AI): enqueue the real summarisation job; this only records the request.
            doc.AiSummaryStatus = AiSummaryStatus.Queued;
            doc.AiSummary = null;
            doc.Version++;
            doc.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return mapper.ToDto(doc);
    }

    /// <summary>GET /documents/:id/file — the signed download URL for locally stored bytes.</summary>
    public async Task<(Stream Body, string ContentType)> OpenAsync(string id, bool signatureValid, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.Deleted, ct)
                  ?? throw AppException.NotFound("Document");
        if (!signatureValid) await access.RequireReadAsync(doc.PatientId, ct);
        if (doc.Status != DocumentStatus.Uploaded || !await storage.ExistsAsync(doc, ct))
            throw AppException.NotFound("Document file");
        return await storage.OpenAsync(doc, ct);
    }

    public bool SignatureValid(string id, string purpose, string? exp, string? sig) =>
        signer.IsValid(id, purpose, exp, sig, clock.UtcNow);
}
