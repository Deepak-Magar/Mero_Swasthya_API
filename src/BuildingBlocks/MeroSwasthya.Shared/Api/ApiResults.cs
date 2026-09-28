using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MeroSwasthya.Shared.Api;

/// <summary>The only way an endpoint returns success: always wrapped in the A.1 envelope.</summary>
public static class ApiResults
{
    public static Ok<ApiEnvelope<T>> Ok<T>(T data) => TypedResults.Ok(new ApiEnvelope<T>(data));
}
