using System.Net;
using System.Net.Http.Headers;
using MeroSwasthya.ContractTests.Infrastructure;
using Xunit.Abstractions;

namespace MeroSwasthya.ContractTests;

/// <summary>
/// A.4 "Documents (paper capture)": presign → PUT → complete → GET, over the storage the factory
/// selected (Testcontainers MinIO when Docker exists, otherwise the local-disk dev fallback).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentsTests
{
    public static readonly string[] DocumentKeys =
    [
        "id", "patientId", "uploadedByUserId", "type", "title", "takenAt", "status", "downloadUrl",
        "aiSummary", "aiSummaryStatus", "version", "updatedAt", "deleted",
    ];

    private readonly ApiFactory _factory;
    private readonly ApiClient _api;
    private readonly Scenario _s;

    public DocumentsTests(ApiFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _api = new ApiClient(factory.CreateClient());
        _s = new Scenario(factory, _api);
        output.WriteLine($"Document storage under test: {factory.StorageMode}");
    }

    private Task<ApiResponse> Presign(Session who, string patientId, string id, long size = 240_000, string contentType = "image/jpeg") =>
        _api.Post("/documents/presign", new
        {
            id, patientId, type = "discharge", title = "Bharatpur Hospital discharge sheet",
            takenAt = "2026-07-02", contentType, sizeBytes = size,
        }, who.AccessToken);

    [Fact]
    public async Task Presign_returns_the_part_A_shape()
    {
        var family = await _s.NewFamily();
        var id = $"d_{Guid.NewGuid()}";
        var data = (await Presign(family.Owner, family.PatientId, id)).Data();

        JsonAssert.HasExactKeys(data, "document", "uploadUrl", "uploadMethod", "uploadHeaders", "expiresInSec");
        JsonAssert.HasExactKeys(data["document"], DocumentKeys);
        JsonAssert.DeepEqual(data["document"], Json.Obj($$"""
            {
              "id": "{{id}}", "patientId": "{{family.PatientId}}", "uploadedByUserId": "{{family.Owner.UserId}}",
              "type": "discharge", "title": "Bharatpur Hospital discharge sheet", "takenAt": "2026-07-02",
              "status": "pending_upload", "downloadUrl": null, "aiSummary": null, "aiSummaryStatus": "none",
              "version": 1, "updatedAt": "<server>", "deleted": false
            }
            """), "updatedAt");
        data["uploadMethod"]!.GetValue<string>().Should().Be("PUT");
        JsonAssert.DeepEqual(data["uploadHeaders"], Json.Obj("""{ "Content-Type": "image/jpeg" }"""));
        data["expiresInSec"]!.GetValue<int>().Should().Be(900);
        Uri.IsWellFormedUriString(data["uploadUrl"]!.GetValue<string>(), UriKind.Absolute).Should().BeTrue();
    }

    [Fact]
    public async Task Presign_complete_and_download_round_trip()
    {
        var family = await _s.NewFamily();
        var worker = await _s.WorkerWithGrant(family);
        var bytes = Scenario.Jpeg(4096);

        var doc = await _s.UploadDocument(worker, family.PatientId, bytes);

        JsonAssert.HasExactKeys(doc, DocumentKeys);
        doc["status"]!.GetValue<string>().Should().Be("uploaded");
        doc["version"]!.GetValue<int>().Should().Be(2);
        doc["uploadedByUserId"]!.GetValue<string>().Should().Be(worker.UserId);

        // The presigned/signed download URL works without a bearer token (the app uses Image.network).
        using var download = await _s.SendToUrl(HttpMethod.Get, doc["downloadUrl"]!.GetValue<string>());
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        var fetched = (await _api.Get($"/documents/{doc["id"]}", family.Owner.AccessToken)).Data();
        JsonAssert.HasExactKeys(fetched, "document");
        JsonAssert.DeepEqual(fetched["document"], doc, "downloadUrl"); // a fresh URL on every read

        (await _s.AuditActions(family)).First().Should().Be("document_added");
    }

    [Fact]
    public async Task Complete_before_the_upload_is_a_rule_violation()
    {
        var family = await _s.NewFamily();
        var id = $"d_{Guid.NewGuid()}";
        (await Presign(family.Owner, family.PatientId, id)).Data();
        (await _api.Post($"/documents/{id}/complete", null, family.Owner.AccessToken)).Error((HttpStatusCode)422, "RULE_VIOLATION");
    }

    [Fact]
    public async Task Complete_is_idempotent_and_audits_once()
    {
        var family = await _s.NewFamily();
        var doc = await _s.UploadDocument(family.Owner, family.PatientId, Scenario.Jpeg());
        var again = (await _api.Post($"/documents/{doc["id"]}/complete", null, family.Owner.AccessToken)).Data()["document"];
        JsonAssert.DeepEqual(again, doc, "downloadUrl");
        (await _s.AuditActions(family)).Count(a => a == "document_added").Should().Be(1);
    }

    [Theory]
    [InlineData(3_000_000, "image/jpeg", "sizeBytes")]
    [InlineData(1000, "application/pdf", "contentType")]
    public async Task Presign_enforces_size_and_type(long size, string contentType, string field)
    {
        var family = await _s.NewFamily();
        (await Presign(family.Owner, family.PatientId, $"d_{Guid.NewGuid()}", size, contentType))
            .Details(HttpStatusCode.BadRequest, "VALIDATION_ERROR").ContainsKey(field).Should().BeTrue();
    }

    [Fact]
    public async Task Presign_needs_append_access()
    {
        var family = await _s.NewFamily();
        var stranger = await TestUsers.NewAccount(_api);
        (await Presign(stranger, family.PatientId, $"d_{Guid.NewGuid()}")).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        var reader = await _s.WorkerWithGrant(family, scope: "read");
        (await Presign(reader, family.PatientId, $"d_{Guid.NewGuid()}")).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Proxy_upload_url_rejects_a_tampered_signature()
    {
        if (!_factory.StorageMode.StartsWith("Local")) return; // proxy URL is only handed out without presigned MinIO

        var family = await _s.NewFamily();
        var id = $"d_{Guid.NewGuid()}";
        var url = (await Presign(family.Owner, family.PatientId, id)).Data()["uploadUrl"]!.GetValue<string>();
        using var response = await _s.SendToUrl(HttpMethod.Put, url.Replace("sig=", "sig=x"), Scenario.Jpeg());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Oversize_upload_is_rejected()
    {
        var family = await _s.NewFamily();
        var id = $"d_{Guid.NewGuid()}";
        var url = (await Presign(family.Owner, family.PatientId, id)).Data()["uploadUrl"]!.GetValue<string>();
        if (!url.Contains("/api/v1/")) return; // presigned MinIO URL: the size limit is MinIO's business

        using var response = await _s.SendToUrl(HttpMethod.Put, url, new byte[2 * 1024 * 1024 + 10]);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dev_multipart_upload_fallback_works()
    {
        var family = await _s.NewFamily();
        var id = $"d_{Guid.NewGuid()}";
        (await Presign(family.Owner, family.PatientId, id)).Data();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Scenario.Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "page.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/documents/{id}/upload") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", family.Owner.AccessToken);
        using var response = await _factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await _api.Post($"/documents/{id}/complete", null, family.Owner.AccessToken)).Data()["document"]!["status"]!
            .GetValue<string>().Should().Be("uploaded");
    }

    [Fact]
    public async Task Summarize_is_501_when_AI_is_off()
    {
        var family = await _s.NewFamily();
        var doc = await _s.UploadDocument(family.Owner, family.PatientId, Scenario.Jpeg());
        (await _api.Post($"/documents/{doc["id"]}/summarize", null, family.Owner.AccessToken))
            .Error(HttpStatusCode.NotImplemented, "NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Read_access_is_required_for_a_document()
    {
        var family = await _s.NewFamily();
        var doc = await _s.UploadDocument(family.Owner, family.PatientId, Scenario.Jpeg());
        var stranger = await TestUsers.NewAccount(_api);
        (await _api.Get($"/documents/{doc["id"]}", stranger.AccessToken)).Error(HttpStatusCode.Forbidden, "FORBIDDEN");
        (await _api.Get("/documents/d_missing", stranger.AccessToken)).Error(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Documents_appear_on_the_timeline()
    {
        var family = await _s.NewFamily();
        var doc = await _s.UploadDocument(family.Owner, family.PatientId, Scenario.Jpeg());
        var item = (await _api.Get($"/patients/{family.PatientId}/timeline", family.Owner.AccessToken)).Data()["items"]!
            .AsArray().Single()!;

        item["kind"]!.GetValue<string>().Should().Be("document");
        item["at"]!.GetValue<string>().Should().Be("2026-07-02T00:00:00.000Z");
        item["title"]!.GetValue<string>().Should().Be("Bharatpur Hospital discharge sheet");
        item["subtitle"]!.GetValue<string>().Should().Be("Discharge sheet");
        item["refId"]!.GetValue<string>().Should().Be(doc["id"]!.GetValue<string>());
        JsonAssert.DeepEqual(item["payload"], doc, "downloadUrl");
    }

    [Fact]
    public async Task Seeded_documents_for_Ram_are_downloadable()
    {
        var owner = await TestUsers.Login(_api, TestUsers.PatientPhone);
        var doc = (await _api.Get("/documents/d_e5e5e5e5-0000-4000-8000-000000000002", owner.AccessToken)).Data()["document"]!;
        doc["title"]!.GetValue<string>().Should().Be("Bharatpur Hospital discharge sheet");
        doc["status"]!.GetValue<string>().Should().Be("uploaded");

        using var download = await _s.SendToUrl(HttpMethod.Get, doc["downloadUrl"]!.GetValue<string>());
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes[..2].Should().Equal(0xFF, 0xD8); // a real JPEG
    }
}
