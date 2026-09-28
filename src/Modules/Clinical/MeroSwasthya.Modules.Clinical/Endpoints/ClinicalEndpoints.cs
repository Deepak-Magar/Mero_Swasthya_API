using MeroSwasthya.Modules.Clinical.Application;
using MeroSwasthya.Modules.Clinical.Contracts;
using MeroSwasthya.Shared.Api;
using MeroSwasthya.Shared.Errors;
using MeroSwasthya.Shared.Paging;
using MeroSwasthya.Shared.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MeroSwasthya.Modules.Clinical.Endpoints;

/// <summary>A.4 "Visits" and "Documents (paper capture)" + the upload/download capability endpoints.</summary>
internal static class ClinicalEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var visits = api.MapGroup("/patients/{id}/visits").WithTags("Visits").RequireAuthorization();

        visits.MapPost("", async (string id, CreateVisitRequest body, VisitService svc, CancellationToken ct) =>
                ApiResults.Ok(new VisitResponse(await svc.CreateAsync(id, body, ct))))
            .Validate<CreateVisitRequest>();

        visits.MapGet("", async (string id, string? limit, VisitService svc, CancellationToken ct) =>
            ApiResults.Ok(new ItemsResponse<VisitDto>(await svc.ListAsync(id, Cursors.Limit(limit, 50, 200), ct))));

        var documents = api.MapGroup("/documents").WithTags("Documents");

        documents.MapPost("/presign", async (PresignDocumentRequest body, DocumentService svc, CancellationToken ct) =>
                ApiResults.Ok(await svc.PresignAsync(body, ct)))
            .Validate<PresignDocumentRequest>()
            .RequireAuthorization();

        documents.MapPost("/{id}/complete", async (string id, DocumentService svc, CancellationToken ct) =>
                ApiResults.Ok(new DocumentResponse(await svc.CompleteAsync(id, ct))))
            .RequireAuthorization();

        documents.MapGet("/{id}", async (string id, DocumentService svc, CancellationToken ct) =>
                ApiResults.Ok(new DocumentResponse(await svc.GetAsync(id, ct))))
            .RequireAuthorization();

        documents.MapPost("/{id}/summarize", async (string id, DocumentService svc, CancellationToken ct) =>
                ApiResults.Ok(new DocumentResponse(await svc.SummarizeAsync(id, ct))))
            .RequireAuthorization();

        // The presign response's uploadUrl in proxy mode: raw bytes, authorised by the URL signature
        // (or a bearer token with append access). The app sends the bytes exactly as it would to S3.
        documents.MapPut("/{id}/upload", async (
                string id, string? exp, string? sig, HttpRequest request, DocumentService svc, CancellationToken ct) =>
            {
                var bytes = await ReadBodyAsync(request.Body, ct);
                var signed = svc.SignatureValid(id, "put", exp, sig);
                return ApiResults.Ok(new DocumentResponse(await svc.ReceiveAsync(id, bytes, request.ContentType, signed, ct)));
            })
            .AllowAnonymous();

        // downloadUrl for locally stored bytes (Image.network sends no bearer, so the URL carries a signature).
        documents.MapGet("/{id}/file", async (string id, string? exp, string? sig, DocumentService svc, CancellationToken ct) =>
            {
                var (body, contentType) = await svc.OpenAsync(id, svc.SignatureValid(id, "get", exp, sig), ct);
                return Results.Stream(body, contentType);
            })
            .AllowAnonymous();

        // DEV ONLY: multipart upload (field "file") for testing without MinIO or a presign client, e.g.
        //   curl -H "Authorization: Bearer …" -F file=@photo.jpg http://127.0.0.1:5000/api/v1/documents/{id}/upload
        var env = api.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment() || env.IsEnvironment("Testing"))
        {
            documents.MapPost("/{id}/upload", async (string id, HttpRequest request, DocumentService svc, CancellationToken ct) =>
                {
                    if (!request.HasFormContentType) throw AppException.Validation("file", "Send multipart/form-data with a \"file\" field");
                    var form = await request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("file") ?? throw AppException.Validation("file", "Missing \"file\" field");
                    await using var stream = file.OpenReadStream();
                    var bytes = await ReadBodyAsync(stream, ct);
                    return ApiResults.Ok(new DocumentResponse(await svc.ReceiveAsync(id, bytes, file.ContentType, signatureValid: false, ct)));
                })
                .RequireAuthorization();
        }
    }

    /// <summary>Reads at most 2 MB + 1 byte, so an oversize body is detected without buffering all of it.</summary>
    private static async Task<byte[]> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > StorageOptions.MaxBytes) break;
        }
        return buffer.ToArray();
    }
}
