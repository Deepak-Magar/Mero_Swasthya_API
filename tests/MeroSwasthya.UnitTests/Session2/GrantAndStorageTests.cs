using MeroSwasthya.Modules.Clinical.Application;
using MeroSwasthya.Modules.Grants.Application;
using MeroSwasthya.Modules.Grants.Domain;
using MeroSwasthya.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeroSwasthya.UnitTests.Session2;

public sealed class GrantAndStorageTests
{
    private static readonly JwtSettings Settings = new()
    {
        AccessSigningKey = "unit-access-signing-key-0123456789abcdef",
        GrantSigningKey = "unit-grant-signing-key-fedcba9876543210",
    };

    private static readonly SigningKeys Keys = new(Settings);

    private static AccessGrant Grant() => new()
    {
        Id = "g_1", PatientId = "p_1", Scope = GrantScope.Append, Jti = "jti-1", CreatedByUserId = "u_1",
        CreatedAt = new DateTime(2026, 9, 18, 4, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2026, 9, 18, 4, 10, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Grant_token_round_trips_even_after_expiry()
    {
        var tokens = new GrantTokens(Settings, Keys);
        var claims = await tokens.ReadAsync("SWC1:" + tokens.Create(Grant()));
        claims.Should().Be(new GrantClaims("g_1", "p_1", GrantScope.Append, "jti-1"));
    }

    [Fact]
    public async Task A_token_signed_with_the_access_key_is_not_a_grant()
    {
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Settings.Issuer,
            Audience = GrantTokens.Audience,
            Claims = new Dictionary<string, object> { ["typ"] = "grant", ["gid"] = "g_1", ["pid"] = "p_1", ["scope"] = "append", ["jti"] = "x" },
            SigningCredentials = new SigningCredentials(Keys.Access, SecurityAlgorithms.HmacSha256),
        });
        (await new GrantTokens(Settings, Keys).ReadAsync("SWC1:" + forged)).Should().BeNull();
    }

    [Fact]
    public async Task Grant_key_signed_token_with_the_wrong_typ_is_rejected()
    {
        var wrongType = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Settings.Issuer,
            Audience = GrantTokens.Audience,
            Claims = new Dictionary<string, object> { ["typ"] = "access", ["gid"] = "g_1", ["pid"] = "p_1", ["scope"] = "append", ["jti"] = "x" },
            SigningCredentials = new SigningCredentials(Keys.Grant, SecurityAlgorithms.HmacSha256),
        });
        (await new GrantTokens(Settings, Keys).ReadAsync("SWC1:" + wrongType)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("SWC2:abc")]
    [InlineData("SWC1:")]
    public async Task Payloads_without_the_SWC1_prefix_or_a_token_are_rejected(string payload)
    {
        (await new GrantTokens(Settings, Keys).ReadAsync(payload)).Should().BeNull();
    }

    private static DocumentUrlSigner Signer() =>
        new(Keys, new StorageOptions { PublicApiBaseUrl = "http://127.0.0.1:5000/api/v1" }, new HttpContextAccessor());

    [Fact]
    public void Signed_document_urls_verify_and_expire()
    {
        var now = new DateTime(2026, 9, 18, 4, 0, 0, DateTimeKind.Utc);
        var url = new Uri(Signer().UploadUrl("d_1", now));
        url.GetLeftPart(UriPartial.Path).Should().Be("http://127.0.0.1:5000/api/v1/documents/d_1/upload");

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
        string exp = query["exp"]!, sig = query["sig"]!;
        Signer().IsValid("d_1", "put", exp, sig, now).Should().BeTrue();
        Signer().IsValid("d_1", "get", exp, sig, now).Should().BeFalse("purpose is part of the signature");
        Signer().IsValid("d_2", "put", exp, sig, now).Should().BeFalse("document id is part of the signature");
        Signer().IsValid("d_1", "put", exp, sig, now.AddMinutes(16)).Should().BeFalse("upload URLs live 15 minutes");
        Signer().IsValid("d_1", "put", (long.Parse(exp) + 1).ToString(), sig, now).Should().BeFalse();
    }

    [Fact]
    public void MinIO_presigned_urls_use_the_public_endpoint_and_sigv4()
    {
        var store = new S3DocumentStore(new S3Options
        {
            Endpoint = "http://127.0.0.1:9000",
            PublicEndpoint = "http://192.168.1.20:9000",
            Bucket = "swc-documents",
        }, NullLogger<S3DocumentStore>.Instance);

        var put = new Uri(store.PresignPut("patients/p_1/d_1.jpg", "image/jpeg"));
        put.GetLeftPart(UriPartial.Path).Should().Be("http://192.168.1.20:9000/swc-documents/patients/p_1/d_1.jpg");
        put.Query.Should().Contain("X-Amz-Algorithm=AWS4-HMAC-SHA256").And.Contain("X-Amz-Expires=900").And.Contain("X-Amz-Signature=");

        var get = new Uri(store.PresignGet("patients/p_1/d_1.jpg"));
        get.Host.Should().Be("192.168.1.20");
        get.Query.Should().Contain("X-Amz-Expires=3600");
    }

    [Fact]
    public void Object_keys_follow_the_patients_prefix_layout()
    {
        DocumentStorage.ObjectKey("p_1", "d_1", "image/jpeg").Should().Be("patients/p_1/d_1.jpg");
        DocumentStorage.ObjectKey("p_1", "d_1", "image/png").Should().Be("patients/p_1/d_1.png");
    }
}
