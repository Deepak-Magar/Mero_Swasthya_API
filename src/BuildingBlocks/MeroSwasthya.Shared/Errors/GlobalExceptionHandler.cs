using System.Text.Json;
using MeroSwasthya.Shared.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MeroSwasthya.Shared.Errors;

/// <summary>Every exception leaves the API as an A.1 error envelope. Nothing else ever reaches the client.</summary>
public sealed class GlobalExceptionHandler(
    IOptions<JsonOptions> json,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, error) = Map(exception);
        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
        else
            logger.LogInformation("Request failed with {Code}: {Message}", error.Code, error.Message);

        if (http.Response.HasStarted) return false;
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new ApiErrorEnvelope(error), json.Value.SerializerOptions, ct);
        return true;
    }

    internal static readonly IReadOnlyDictionary<string, string> EmptyDetails = new Dictionary<string, string>();

    internal static (int Status, ApiError Error) Map(Exception exception) => exception switch
    {
        AppException app => (app.Code.HttpStatus(),
            new ApiError(app.Code.Wire(), app.Message, app.Details ?? EmptyDetails)),
        BadHttpRequestException bad => (400, FromBadRequest(bad)),
        JsonException jsonEx => (400, FromJson(jsonEx)),
        _ => (500, new ApiError(ErrorCode.Internal.Wire(), "Unexpected server error", EmptyDetails)),
    };

    private static ApiError FromBadRequest(BadHttpRequestException bad)
    {
        if (bad.InnerException is JsonException jsonEx) return FromJson(jsonEx);

        // Missing body, wrong content type, or an unparsable query / route value such as ?limit=abc.
        string field;
        string message;
        if (bad.Message.Contains("from body", StringComparison.OrdinalIgnoreCase) ||
            bad.Message.Contains("content type", StringComparison.OrdinalIgnoreCase))
        {
            field = "body";
            message = "A JSON request body is required";
        }
        else
        {
            // "Failed to bind parameter \"int limit\" from \"abc\"."
            var match = System.Text.RegularExpressions.Regex.Match(bad.Message, "parameter \"[^\" ]+ ([^\"]+)\"");
            field = match.Success ? match.Groups[1].Value : "request";
            message = "Invalid value";
        }

        return new ApiError(ErrorCode.ValidationError.Wire(), $"{message} ({field})",
            new Dictionary<string, string> { [field] = message });
    }

    private static ApiError FromJson(JsonException jsonEx)
    {
        var field = JsonPathToField(jsonEx.Path);
        var message = field == "body" ? "Malformed JSON body" : "Invalid value";
        return new ApiError(ErrorCode.ValidationError.Wire(), $"{message} ({field})",
            new Dictionary<string, string> { [field] = message });
    }

    /// <summary><c>$.birthPlan.facilityId</c> → <c>birthPlan.facilityId</c>; <c>$</c> or null → <c>body</c>.</summary>
    internal static string JsonPathToField(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "$") return "body";
        var trimmed = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
        return trimmed.Length == 0 ? "body" : trimmed;
    }
}
