using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeroSwasthya.ContractTests.Infrastructure;

/// <summary>Raw HTTP response: status plus the parsed JSON body, so tests assert on the wire shape.</summary>
public sealed record ApiResponse(HttpStatusCode Status, JsonNode? Body, string Raw)
{
    /// <summary>Asserts <c>{ ok: true, data: {…} }</c> with the given status and returns <c>data</c>.</summary>
    public JsonObject Data(HttpStatusCode expected = HttpStatusCode.OK)
    {
        Status.Should().Be(expected, Raw);
        var body = Body.Should().BeOfType<JsonObject>().Subject;
        body.Select(p => p.Key).Should().BeEquivalentTo(["ok", "data"], "success envelope is exactly { ok, data }");
        body["ok"]!.GetValue<bool>().Should().BeTrue();
        return body["data"].Should().BeOfType<JsonObject>("data is always an object, never a bare array").Subject;
    }

    /// <summary>Asserts <c>{ ok: false, error: { code, message, details } }</c> and returns <c>error</c>.</summary>
    public JsonObject Error(HttpStatusCode expected, string code)
    {
        Status.Should().Be(expected, Raw);
        var body = Body.Should().BeOfType<JsonObject>().Subject;
        body.Select(p => p.Key).Should().BeEquivalentTo(["ok", "error"]);
        body["ok"]!.GetValue<bool>().Should().BeFalse();
        var error = body["error"].Should().BeOfType<JsonObject>().Subject;
        error.Select(p => p.Key).Should().BeEquivalentTo(["code", "message", "details"]);
        error["code"]!.GetValue<string>().Should().Be(code, Raw);
        error["details"].Should().BeOfType<JsonObject>();
        return error;
    }

    public JsonObject Details(HttpStatusCode expected, string code) => Error(expected, code)["details"]!.AsObject();
}

public sealed class ApiClient(HttpClient http)
{
    public const string Prefix = "/api/v1";

    public string? Token { get; set; }

    public Task<ApiResponse> Get(string path, string? token = null) => Send(HttpMethod.Get, path, null, token);

    public Task<ApiResponse> Post(string path, object? body = null, string? token = null) =>
        Send(HttpMethod.Post, path, body ?? new { }, token);

    public Task<ApiResponse> Patch(string path, object body, string? token = null) =>
        Send(HttpMethod.Patch, path, body, token);

    public Task<ApiResponse> PostRaw(string path, string rawJson, string? token = null) =>
        SendContent(HttpMethod.Post, path, new StringContent(rawJson, Encoding.UTF8, "application/json"), token);

    public async Task<ApiResponse> Send(HttpMethod method, string path, object? body, string? token = null)
    {
        HttpContent? content = null;
        if (body is not null)
        {
            var json = body is JsonNode node ? node.ToJsonString() : JsonSerializer.Serialize(body, Json.Web);
            content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        return await SendContent(method, path, content, token);
    }

    private async Task<ApiResponse> SendContent(HttpMethod method, string path, HttpContent? content, string? token)
    {
        using var request = new HttpRequestMessage(method, Prefix + path) { Content = content };
        var bearer = token ?? Token;
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        JsonNode? parsed = null;
        if (raw.Length > 0)
        {
            try { parsed = JsonNode.Parse(raw); } catch (JsonException) { /* asserted by the caller */ }
        }
        return new ApiResponse(response.StatusCode, parsed, raw);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();
}
