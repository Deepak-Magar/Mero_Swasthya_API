using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using MeroSwasthya.Modules.Clinical.Domain;
using MeroSwasthya.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeroSwasthya.Modules.Clinical.Application;

/// <summary>Bound from <c>S3</c>. Empty <see cref="Endpoint"/> = no object storage configured.</summary>
internal sealed class S3Options
{
    public const string Section = "S3";

    /// <summary>Where the API talks to MinIO, e.g. http://127.0.0.1:9000.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Host the phone can reach, used only inside presigned URLs, e.g. http://192.168.1.20:9000. Defaults to <see cref="Endpoint"/>.</summary>
    public string PublicEndpoint { get; set; } = "";
    public string AccessKey { get; set; } = "minio";
    public string SecretKey { get; set; } = "minio123";
    public string Bucket { get; set; } = "swc-documents";
    public string Region { get; set; } = "us-east-1";
}

/// <summary>Bound from <c>Storage</c>.</summary>
internal sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>"auto" (S3 when reachable, else local disk) | "s3" | "local".</summary>
    public string Mode { get; set; } = "auto";

    /// <summary>
    /// "proxy" (default): <c>uploadUrl</c> is this API's signed PUT endpoint and the server writes the bytes
    /// to S3 (or disk). "presigned": <c>uploadUrl</c> is a MinIO presigned PUT. See docs/APP_INTEGRATION.md
    /// for why proxy is the default with the current app build.
    /// </summary>
    public string UploadMode { get; set; } = "proxy";

    /// <summary>Dev fallback directory (relative paths are under the content root).</summary>
    public string LocalPath { get; set; } = ".data/documents";

    /// <summary>Absolute base for API URLs handed to the phone (e.g. behind a tunnel). Default: the request's own host.</summary>
    public string? PublicApiBaseUrl { get; set; }

    public const long MaxBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan UploadTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DownloadTtl = TimeSpan.FromHours(1);
    public static readonly string[] ContentTypes = ["image/jpeg", "image/png"];
}

/// <summary>MinIO / S3 via AWSSDK.S3 (path-style, SigV4). Presigned URLs are signed for <see cref="S3Options.PublicEndpoint"/>.</summary>
internal sealed class S3DocumentStore
{
    private static readonly TimeSpan ProbeTtl = TimeSpan.FromSeconds(30);

    private readonly S3Options _options;
    private readonly ILogger<S3DocumentStore> _logger;
    private readonly AmazonS3Client? _client;
    private readonly AmazonS3Client? _presigner;
    private readonly bool _publicIsHttp;
    private (bool Ok, DateTime At) _probe = (false, DateTime.MinValue);
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    public S3DocumentStore(S3Options options, ILogger<S3DocumentStore> logger)
    {
        _options = options;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(options.Endpoint)) return;

        AWSConfigsS3.UseSignatureVersion4 = true;
        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        _client = new AmazonS3Client(credentials, Config(options.Endpoint, TimeSpan.FromSeconds(5)));
        var publicEndpoint = string.IsNullOrWhiteSpace(options.PublicEndpoint) ? options.Endpoint : options.PublicEndpoint;
        _presigner = new AmazonS3Client(credentials, Config(publicEndpoint, null));
        _publicIsHttp = publicEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    private AmazonS3Config Config(string serviceUrl, TimeSpan? timeout)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = _options.Region,
            MaxErrorRetry = 0,
        };
        if (timeout is not null) config.Timeout = timeout;
        return config;
    }

    public bool Configured => _client is not null;

    /// <summary>Reachable and bucket present (created if missing). Cached for 30 s.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (_client is null) return false;
        if (DateTime.UtcNow - _probe.At < ProbeTtl) return _probe.Ok;

        await _probeLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _probe.At < ProbeTtl) return _probe.Ok;
            var ok = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                if (!await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.Bucket))
                    await _client.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, timeout.Token);
                ok = true;
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Object storage at {Endpoint} is not reachable ({Error}); documents fall back to local disk",
                    _options.Endpoint, e.GetBaseException().Message);
            }
            _probe = (ok, DateTime.UtcNow);
            return ok;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    public string PresignPut(string key, string contentType) => _presigner!.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = _options.Bucket,
        Key = key,
        Verb = HttpVerb.PUT,
        ContentType = contentType,
        Expires = DateTime.UtcNow.Add(StorageOptions.UploadTtl),
        Protocol = _publicIsHttp ? Protocol.HTTP : Protocol.HTTPS,
    });

    public string PresignGet(string key) => _presigner!.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = _options.Bucket,
        Key = key,
        Verb = HttpVerb.GET,
        Expires = DateTime.UtcNow.Add(StorageOptions.DownloadTtl),
        Protocol = _publicIsHttp ? Protocol.HTTP : Protocol.HTTPS,
    });

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client!.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        await _client!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            UseChunkEncoding = false, // MinIO-friendly single-part upload
        }, ct);
    }

    public async Task<(Stream Body, string ContentType)> OpenAsync(string key, CancellationToken ct)
    {
        var response = await _client!.GetObjectAsync(_options.Bucket, key, ct);
        return (response.ResponseStream, response.Headers.ContentType);
    }
}

/// <summary>Development fallback: document bytes on local disk under <see cref="StorageOptions.LocalPath"/>.</summary>
internal sealed class LocalDocumentStore(StorageOptions options, IHostEnvironment env)
{
    private string Root => Path.IsPathRooted(options.LocalPath)
        ? options.LocalPath
        : Path.Combine(env.ContentRootPath, options.LocalPath);

    private string PathFor(string key) =>
        Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar));

    public bool Exists(string key) => File.Exists(PathFor(key));

    public async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public Stream Open(string key) => File.OpenRead(PathFor(key));
}

/// <summary>
/// Capability URLs on this API (upload PUT and download GET) for phones that cannot send a bearer
/// token with the request (Image.network) or that do send one where S3 would reject it.
/// HMAC-SHA256 over "purpose:documentId:exp" with a key derived from the access signing key.
/// </summary>
internal sealed class DocumentUrlSigner(SigningKeys keys, StorageOptions options, IHttpContextAccessor http)
{
    private readonly byte[] _key = HMACSHA256.HashData(keys.Access.Key, "mero-swasthya/document-urls"u8.ToArray());

    public string UploadUrl(string documentId, DateTime now) => Build(documentId, "upload", "put", now + StorageOptions.UploadTtl);

    public string DownloadUrl(string documentId, DateTime now) => Build(documentId, "file", "get", now + StorageOptions.DownloadTtl);

    public bool IsValid(string documentId, string purpose, string? exp, string? sig, DateTime now)
    {
        if (!long.TryParse(exp, out var expSeconds) || sig is null) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime < now) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(purpose, documentId, expSeconds));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(sig));
    }

    private string Build(string documentId, string path, string purpose, DateTime expires)
    {
        var exp = new DateTimeOffset(expires).ToUnixTimeSeconds();
        return $"{BaseUrl()}/documents/{Uri.EscapeDataString(documentId)}/{path}?exp={exp}&sig={Sign(purpose, documentId, exp)}";
    }

    private string Sign(string purpose, string documentId, long exp) =>
        WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{documentId}:{exp}")));

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(options.PublicApiBaseUrl)) return options.PublicApiBaseUrl.TrimEnd('/');
        var request = http.HttpContext?.Request;
        return request is null
            ? "http://127.0.0.1:5000/api/v1"
            : $"{request.Scheme}://{request.Host}{request.PathBase}/api/v1";
    }
}

/// <summary>Decides where a document's bytes go and which URLs the phone gets.</summary>
internal sealed class DocumentStorage(
    S3DocumentStore s3,
    LocalDocumentStore local,
    DocumentUrlSigner signer,
    StorageOptions options)
{
    public const string S3 = "s3";
    public const string Local = "local";

    public async Task<bool> UseS3Async(CancellationToken ct) => options.Mode.ToLowerInvariant() switch
    {
        "local" => false,
        "s3" => s3.Configured ? true : throw new InvalidOperationException("Storage:Mode=s3 but S3:Endpoint is not configured"),
        _ => await s3.IsAvailableAsync(ct),
    };

    /// <summary>The URL the phone PUTs the bytes to (A.4 presign). May set <see cref="Document.Storage"/>.</summary>
    public async Task<string> UploadUrlAsync(Document doc, DateTime now, CancellationToken ct)
    {
        if (string.Equals(options.UploadMode, "presigned", StringComparison.OrdinalIgnoreCase) && await UseS3Async(ct))
        {
            doc.Storage = S3;
            return s3.PresignPut(doc.ObjectKey, doc.ContentType);
        }
        return signer.UploadUrl(doc.Id, now);
    }

    /// <summary>Bytes that arrived at this API (proxy upload or the multipart dev fallback).</summary>
    public async Task StoreAsync(Document doc, byte[] bytes, CancellationToken ct)
    {
        if (await UseS3Async(ct))
        {
            await s3.PutAsync(doc.ObjectKey, bytes, doc.ContentType, ct);
            doc.Storage = S3;
        }
        else
        {
            await local.PutAsync(doc.ObjectKey, bytes, ct);
            doc.Storage = Local;
        }
    }

    /// <summary>The HEAD check behind POST /documents/:id/complete.</summary>
    public async Task<bool> ExistsAsync(Document doc, CancellationToken ct) => doc.Storage switch
    {
        S3 => await s3.ExistsAsync(doc.ObjectKey, ct),
        Local => local.Exists(doc.ObjectKey),
        _ => false,
    };

    /// <summary>Fresh 1 h download URL: presigned GET on MinIO, or this API's signed file URL for local storage.</summary>
    public string? DownloadUrl(Document doc, DateTime now) =>
        doc.Status != DocumentStatus.Uploaded ? null
        : doc.Storage == S3 ? s3.PresignGet(doc.ObjectKey)
        : signer.DownloadUrl(doc.Id, now);

    public async Task<(Stream Body, string ContentType)> OpenAsync(Document doc, CancellationToken ct) =>
        doc.Storage == S3 ? await s3.OpenAsync(doc.ObjectKey, ct) : (local.Open(doc.ObjectKey), doc.ContentType);

    public static string ObjectKey(string patientId, string documentId, string contentType) =>
        $"patients/{patientId}/{documentId}.{(contentType == "image/png" ? "png" : "jpg")}";

    public static StorageOptions BindStorage(IConfiguration config) =>
        config.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();

    public static S3Options BindS3(IConfiguration config) =>
        config.GetSection(S3Options.Section).Get<S3Options>() ?? new S3Options();
}
