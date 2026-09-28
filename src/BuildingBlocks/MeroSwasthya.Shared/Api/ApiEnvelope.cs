using System.Text.Json.Serialization;

namespace MeroSwasthya.Shared.Api;

/// <summary>A.1 success envelope: <c>{ "ok": true, "data": { … } }</c>. Data is always an object.</summary>
public sealed record ApiEnvelope<T>(T Data)
{
    [JsonPropertyOrder(-1)]
    public bool Ok => true;
}

/// <summary>A.1 error envelope: <c>{ "ok": false, "error": { code, message, details } }</c>.</summary>
public sealed record ApiErrorEnvelope(ApiError Error)
{
    [JsonPropertyOrder(-1)]
    public bool Ok => false;
}

/// <summary><c>details</c> is always an object (empty when there is nothing to add).</summary>
public sealed record ApiError(string Code, string Message, object Details);
